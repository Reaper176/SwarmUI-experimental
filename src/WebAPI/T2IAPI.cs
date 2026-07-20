using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using Image = SwarmUI.Utils.Image;
using ISImage = SixLabors.ImageSharp.Image;
using ISImageRGBA = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace SwarmUI.WebAPI;

[API.APIClass("API routes for actual text-to-image processing and directly related features.")]
public static class T2IAPI
{
    public static void Register()
    {
        // TODO: Some of these shouldn't be here?
        API.RegisterAPICall(GenerateText2Image, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(GenerateText2ImageWS, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(AddImageToHistory, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(ListImages, false, Permissions.ViewImageHistory);
        API.RegisterAPICall(RescanImageMetadata, true, Permissions.ViewImageHistory);
        API.RegisterAPICall(ToggleImageStarred, true, Permissions.UserStarImages);
        API.RegisterAPICall(ToggleImageHidden, true, Permissions.ViewImageHistory);
        API.RegisterAPICall(SetImageRating, true, Permissions.ViewImageHistory);
        API.RegisterAPICall(SetImageTags, true, Permissions.ViewImageHistory);
        API.RegisterAPICall(SetImageNotes, true, Permissions.ViewImageHistory);
        API.RegisterAPICall(OpenImageFolder, true, Permissions.LocalImageFolder);
        API.RegisterAPICall(DeleteImage, true, Permissions.UserDeleteImage);
        API.RegisterAPICall(BulkMoveImages, true, Permissions.UserDeleteImage);
        API.RegisterAPICall(KritaAPI.SendImageToKrita, true, Permissions.LocalKritaBridge);
        API.RegisterAPICall(KritaAPI.ImportKritaImage, true, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(KritaAPI.CheckPendingKritaImage, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(KritaAPI.GetActiveKritaSession, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(ClassicInpaintAPI.ClassicInpaint, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(ClassicInpaintAPI.GetClassicInpaintBackends, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(ListT2IParams, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(TriggerRefresh, true, Permissions.FundamentalGenerateTabAccess); // Intentionally weird perm here: internal check for readonly vs true refresh
    }

    [API.APIDescription("Generate images from text prompts, with WebSocket updates. This is the most important route inside of Swarm.",
        """
            // A status update, contains a full `GetCurrentStatus` response, but pushed actively whenever status changes during generation
            "status":
            {
                "waiting_gens": 1,
                "loading_models": 0,
                "waiting_backends": 1,
                "live_gens": 0
            },
            "backend_status":
            {
                "status": "running",
                "class": "",
                "message": "",
                "any_loading": false
            },
            "supported_features": ["featureid", ...]

            // A progress update
            "gen_progress":
            {
                "batch_index": "0", // which image index within the batch is being updated here
                "overall_percent": 0.1, // eg how many nodes into a workflow graph, as a fraction from 0 to 1
                "current_percent": 0.0, // how far within the current node, as a fraction from 0 to 1
                "preview": "data:image/jpeg;base64,abc123" // a preview image (data-image-url), if available. If there's no preview, this key is omitted.
            }

            // An image generation result
            "image":
            {
                "image": "View/local/raw/2024-01-02/0304-a photo of a cat-etc-1.png", // the image file path, GET this path to read the image content. In some cases can be a 'data:...' encoded image.
                "batch_index": "0", // which image index within the batch this is
                "metadata": "{ ... }" // image metadata string, usually a JSON blob stringified. Not guaranteed to be.
            }

            // After image generations, sometimes there are images to discard (eg scoring extension may discard images below a certain score)
            "discard_indices": [0, 1, 2, ...] // batch indices of images to discard, if any
        """)]
    public static async Task<JObject> GenerateText2ImageWS(WebSocket socket, Session session,
        [API.APIParameter("The number of images to generate.")] int images,
        [API.APIParameter("Raw mapping of input should contain general T2I parameters (see listing on Generate tab of main interface) to values, eg `{ \"prompt\": \"a photo of a cat\", \"model\": \"OfficialStableDiffusion/sd_xl_base_1.0\", \"steps\": 20, ... }`. Note that this is the root raw map, ie all params go on the same level as `images`, `session_id`, etc.\nThe key 'extra_metadata' may be used to apply extra internal metadata as a JSON string:string map.")] JObject rawInput)
    {
        using CancellationTokenSource cancelTok = new();
        bool retain = false, ended = false;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalProgramCancel, cancelTok.Token);
        SharedGenT2IData data = new();
        ConcurrentDictionary<Task, Task> tasks = [];
        static int guessBatchSize(JObject input)
        {
            if (input.TryGetValue("batchsize", out JToken batch))
            {
                return batch.Value<int>();
            }
            return 1;
        }
        _ = Utilities.RunCheckedTask(async () =>
        {
            try
            {
                int batchOffset = images * guessBatchSize(rawInput);
                while (!cancelTok.IsCancellationRequested)
                {
                    byte[] rec = await socket.ReceiveData(Program.ServerSettings.Network.MaxReceiveBytes, linked.Token);
                    Volatile.Write(ref retain, true);
                    if (socket.State != WebSocketState.Open || cancelTok.IsCancellationRequested || Volatile.Read(ref ended))
                    {
                        return;
                    }
                    JObject newInput = StringConversionHelper.UTF8Encoding.GetString(rec).ParseToJson();
                    int newImages = newInput.Value<int>("images");
                    Task handleMore = API.RunWebsocketHandlerCallWS(GenT2I_Internal, session, (newImages, newInput, data, batchOffset), socket);
                    tasks.TryAdd(handleMore, handleMore);
                    Volatile.Write(ref retain, false);
                    batchOffset += newImages * guessBatchSize(newInput);
                }
            }
            catch (TaskCanceledException)
            {
                return;
            }
            finally
            {
                Volatile.Write(ref retain, false);
            }
        });
        Task handle = API.RunWebsocketHandlerCallWS(GenT2I_Internal, session, (images, rawInput, data, 0), socket);
        tasks.TryAdd(handle, handle);
        while (Volatile.Read(ref retain) || tasks.Any())
        {
            if (tasks.Any())
            {
                await Task.WhenAny(tasks.Keys.ToList());
            }
            foreach (Task t in tasks.Keys.Where(t => t.IsCompleted).ToList())
            {
                tasks.TryRemove(t, out _);
            }
            if (tasks.IsEmpty())
            {
                await socket.SendJson(new JObject() { ["socket_intention"] = "close" }, API.WebsocketTimeout);
                await Task.Delay(TimeSpan.FromSeconds(2)); // Give 2 seconds to allow a new gen request before actually closing
                if (tasks.IsEmpty())
                {
                    Volatile.Write(ref ended, true);
                }
            }
        }
        await socket.SendJson(BasicAPIFeatures.GetCurrentStatusRaw(session), API.WebsocketTimeout);
        return null;
    }

    [API.APIDescription("Generate images from text prompts, directly as an HTTP route. See the examples in the API docs root page.",
        """
            "images":
            [
                "View/local/raw/2024-01-02/0304-a photo of a cat-etc-1.png", // the image file path, GET this path to read the image content. In some cases can be a 'data:...' encoded image.
            ]
        """)]
    public static async Task<JObject> GenerateText2Image(Session session,
        [API.APIParameter("The number of images to generate.")] int images,
        [API.APIParameter("Raw mapping of input should contain general T2I parameters (see listing on Generate tab of main interface) to values, eg `{ \"prompt\": \"a photo of a cat\", \"model\": \"OfficialStableDiffusion/sd_xl_base_1.0\", \"steps\": 20, ... }`. Note that this is the root raw map, ie all params go on the same level as `images`, `session_id`, etc.\nThe key 'extra_metadata' may be used to apply extra internal metadata as a JSON string:string map.")] JObject rawInput)
    {
        List<JObject> outputs = await API.RunWebsocketHandlerCallDirect(GenT2I_Internal, session, (images, rawInput, new SharedGenT2IData(), 0));
        Dictionary<int, string> imageOutputs = [];
        int[] discards = null;
        foreach (JObject obj in outputs)
        {
            if (obj.ContainsKey("error"))
            {
                return obj;
            }
            if (obj.TryGetValue("image", out JToken image) && obj.TryGetValue("batch_index", out JToken index))
            {
                imageOutputs.Add((int)index, image.ToString());
            }
            if (obj.TryGetValue("discard_indices", out JToken discard))
            {
                discards = [.. discard.Values<int>()];
            }
        }
        if (discards is not null)
        {
            foreach (int x in discards)
            {
                imageOutputs.Remove(x);
            }
        }
        return new JObject() { ["images"] = new JArray(imageOutputs.Values.ToArray()) };
    }

    public static HashSet<string> AlwaysTopKeys = [];

    /// <summary>Helper util to take a user-supplied JSON object of parameter data and turn it into a valid T2I request object.</summary>
    public static T2IParamInput RequestToParams(Session session, JObject rawInput, bool applyPresets = true)
    {
        T2IParamInput user_input = new(session);
        List<string> keys = [.. rawInput.Properties().Select(p => p.Name)];
        keys = [.. keys.Where(AlwaysTopKeys.Contains), .. keys.Where(k => !AlwaysTopKeys.Contains(k))];
        if (rawInput.TryGetValue("extra_metadata", out JToken extraMeta) && extraMeta is JObject obj)
        {
            foreach (JProperty prop in obj.Properties())
            {
                user_input.ExtraMeta[prop.Name] = prop.Value.ToString();
            }
        }
        foreach (string key in keys)
        {
            if (key == "session_id" || key == "presets" || key == "extra_metadata")
            {
                // Skip
            }
            else if (T2IParamTypes.TryGetType(key, out _, user_input))
            {
                JToken val = rawInput[key];
                string valStr = val is JArray jarr ? jarr.Select(v => $"{v}").JoinString("\n|||\n") : $"{val}";
                T2IParamTypes.ApplyParameter(key, valStr, user_input);
            }
            else
            {
                Logs.Warning($"T2I image request from user {session.User.UserID} had request parameter '{key}', but that parameter is unrecognized, skipping...");
            }
        }
        if (rawInput.TryGetValue("presets", out JToken presets) && presets.Any())
        {
            foreach (JToken presetName in presets.Values())
            {
                T2IPreset presetObj = session.User.GetPreset(presetName.ToString());
                if (presetObj is null)
                {
                    Logs.Warning($"User {session.User.UserID} tried to use preset '{presetName}', but it does not exist!");
                    continue;
                }
                if (applyPresets)
                {
                    presetObj.ApplyTo(user_input);
                }
                else
                {
                    user_input.PendingPresets.Add(presetObj);
                }
            }
            user_input.ExtraMeta["presets_used"] = presets.Values().Select(v => v.ToString()).ToList();
        }
        return user_input;
    }

    public class SharedGenT2IData
    {
        public int NumExtra, NumNonReal;
    }

    /// <summary>Internal route for generating images.</summary>
    public static async Task GenT2I_Internal(Session session, (int, JObject, SharedGenT2IData, int) input, Action<JObject> output, bool isWS)
    {
        (int images, JObject rawInput, SharedGenT2IData data, int batchOffset) = input;
        using Session.GenClaim claim = session.Claim(gens: images);
        if (isWS)
        {
            output(BasicAPIFeatures.GetCurrentStatusRaw(session));
        }
        bool continueAfterErrors = false;
        void setError(string message)
        {
            Logs.Warning($"Refused to generate image for {session.User.UserID}: {message}");
            if (!continueAfterErrors || claim.ShouldCancel)
            {
                output(new JObject() { ["error"] = message });
                claim.LocalClaimInterrupt.Cancel();
            }
        }
        long timeStart = Environment.TickCount64;
        T2IParamInput user_input;
        try
        {
            user_input = RequestToParams(session, rawInput);
            continueAfterErrors = user_input.Get(T2IParamTypes.ContinueAfterErrors, false);
        }
        catch (SwarmReadableErrorException ex)
        {
            setError(ex.Message);
            return;
        }
        if (user_input.Get(T2IParamTypes.ForwardRawBackendData, false))
        {
            user_input.ReceiveRawBackendData = (type, data) =>
            {
                output(new JObject()
                {
                    ["raw_backend_data"] = new JObject()
                    {
                        ["type"] = type,
                        ["data"] = Convert.ToBase64String(data)
                    }
                });
            };
        }
        user_input.ApplySpecialLogic();
        images = user_input.Get(T2IParamTypes.Images, 1);
        claim.Extend(images - claim.WaitingGenerations);
        Logs.Info($"User {session.User.UserID} requested {images} image{(images == 1 ? "" : "s")} with model '{user_input.Get(T2IParamTypes.Model)?.Name}'...");
        if (Logs.MinimumLevel <= Logs.LogLevel.Verbose)
        {
            Logs.Verbose($"User {session.User.UserID} above image request had parameters: {user_input}");
        }
        List<T2IEngine.ImageOutput> imageSet = [];
        List<Task> tasks = [];
        void removeDoneTasks()
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].IsCompleted)
                {
                    if (tasks[i].IsFaulted)
                    {
                        Logs.Error($"Image generation failed: {tasks[i].Exception}");
                    }
                    tasks.RemoveAt(i--);
                }
            }
        }
        int max_degrees = session.User.CalcMaxT2ISimultaneous;
        List<int> discard = [];
        object discardLock = new();
        int batchSizeExpected = user_input.Get(T2IParamTypes.BatchSize, 1);
        void saveImage(T2IEngine.ImageOutput image, int actualIndex, T2IParamInput thisParams, string metadata)
        {
            Logs.Verbose($"T2IAPI received save request for index {actualIndex} for gen request id {thisParams.UserRequestId}, isreal={image.IsReal}");
            bool noSave = thisParams.Get(T2IParamTypes.DoNotSave, false);
            if (!image.IsReal && thisParams.Get(T2IParamTypes.DoNotSaveIntermediates, false))
            {
                noSave = true;
            }
            string url, filePath;
            if (noSave)
            {
                MediaFile file = image.File;
                if (session.User.Settings.FileFormat.ReformatTransientImages && image.ActualFileTask is not null)
                {
                    file = image.ActualFileTask.Result;
                }
                (url, filePath) = (file.AsDataString(), null);
            }
            else
            {
                (url, filePath) = session.SaveImage(image, actualIndex, thisParams, metadata);
            }
            if (url == "ERROR")
            {
                setError($"Server failed to save an image.");
                return;
            }
            image.RefuseImage = () =>
            {
                if (filePath is not null && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                lock (discardLock)
                {
                    discard.Add(actualIndex);
                }
                lock (imageSet)
                {
                    imageSet.Remove(image);
                }
            };
            lock (imageSet)
            {
                imageSet.Add(image);
            }
            WebhookManager.SendEveryGenWebhook(thisParams, url, image.File);
            if (thisParams.Get(T2IParamTypes.ForwardSwarmData, false))
            {
                output(new JObject() { ["raw_swarm_data"] = new JObject() { ["params_used"] = JArray.FromObject(thisParams.ParamsQueried.ToArray()) } });
            }
            output(new JObject() { ["image"] = url, ["batch_index"] = $"{actualIndex}", ["request_id"] = $"{thisParams.UserRequestId}", ["metadata"] = string.IsNullOrWhiteSpace(metadata) ? null : metadata });
        }
        for (int i = 0; i < images && !claim.ShouldCancel; i++)
        {
            removeDoneTasks();
            while (tasks.Count > max_degrees)
            {
                await Task.WhenAny(tasks);
                removeDoneTasks();
            }
            if (claim.ShouldCancel)
            {
                break;
            }
            int localIndex = i * batchSizeExpected;
            int imageIndex = localIndex + batchOffset;
            T2IParamInput thisParams = user_input.Clone();
            if (!thisParams.Get(T2IParamTypes.NoSeedIncrement, false))
            {
                if (thisParams.TryGet(T2IParamTypes.VariationSeed, out long varSeed) && thisParams.Get(T2IParamTypes.VariationSeedStrength) > 0)
                {
                    thisParams.Set(T2IParamTypes.VariationSeed, varSeed + localIndex);
                }
                else
                {
                    thisParams.Set(T2IParamTypes.Seed, thisParams.Get(T2IParamTypes.Seed) + localIndex);
                }
            }
            int numCalls = 0;
            tasks.Add(Task.Run(() => T2IEngine.CreateImageTask(thisParams, $"{imageIndex}", claim, output, setError, isWS,
                (image, metadata) =>
                {
                    int actualIndex = imageIndex + numCalls;
                    if (image.IsReal)
                    {
                        numCalls++;
                        if (numCalls > batchSizeExpected)
                        {
                            actualIndex = images * batchSizeExpected + Interlocked.Increment(ref data.NumExtra);
                        }
                    }
                    else
                    {
                        actualIndex = -10 - Interlocked.Increment(ref data.NumNonReal);
                    }
                    saveImage(image, actualIndex, thisParams, metadata);
                })));
            if (Program.Backends.QueuedRequests < Program.ServerSettings.Backends.MaxRequestsForcedOrder)
            {
                await Task.Delay(20); // Tiny few-ms delay to encourage tasks retaining order.
            }
        }
        while (tasks.Any())
        {
            Task timeout = Task.Delay(TimeSpan.FromSeconds(30));
            await Task.WhenAny([.. tasks, timeout]);
            output(new JObject() { ["keep_alive"] = true });
            removeDoneTasks();
        }
        long finalTime = Environment.TickCount64;
        T2IEngine.ImageOutput[] griddables = [.. imageSet.Where(i => i.IsReal)];
        if (griddables.Length <= session.User.Settings.MaxImagesInMiniGrid && griddables.Length > 1 && griddables.All(i => i.File.Type.MetaType == MediaMetaType.Image))
        {
            ISImage[] imgs = [.. griddables.Select(i => (i.File as Image).ToIS)];
            int columns = (int)Math.Ceiling(Math.Sqrt(imgs.Length));
            int rows = columns;
            if (griddables.Length <= columns * (columns - 1))
            {
                rows--;
            }
            if (imgs.Length <= GridShapeTable.Length)
            {
                (columns, rows) = GridShapeTable[imgs.Length - 1];
            }
            int widthPerImage = imgs.Max(i => i.Width);
            int heightPerImage = imgs.Max(i => i.Height);
            ISImageRGBA grid = new(widthPerImage * columns, heightPerImage * rows);
            grid.Mutate(m =>
            {
                for (int i = 0; i < imgs.Length; i++)
                {
                    int x = (i % columns) * widthPerImage, y = (i / columns) * heightPerImage;
                    m.DrawImage(imgs[i], new Point(x, y), 1);
                }
            });
            Image gridImg = new(grid);
            long genTime = Environment.TickCount64 - timeStart;
            T2IParamInput finalInput = user_input.Clone();
            finalInput.NoUnusedParams = true;
            finalInput.ExtraMeta["generation_time"] = $"{genTime / 1000.0:0.00} total seconds (average {(finalTime - timeStart) / griddables.Length / 1000.0:0.00} seconds per image)";
            (Task<MediaFile> gridFileTask, string metadata) = finalInput.SourceSession.ApplyMetadata(gridImg, finalInput, imgs.Length);
            T2IEngine.ImageOutput gridOutput = new() { File = gridImg, ActualFileTask = gridFileTask, GenTimeMS = genTime };
            saveImage(gridOutput, -1, finalInput, metadata);
        }
        T2IEngine.PostBatchEvent?.Invoke(new(user_input, [.. griddables]));
        List<int> discardFinal;
        lock (discardLock)
        {
            discardFinal = [.. discard];
        }
        output(new JObject() { ["discard_indices"] = JToken.FromObject(discardFinal) });
        WebhookManager.SendManualAtEndWebhook(user_input);
    }

    public static (int, int)[] GridShapeTable =
        [
            (1, 1), // 1
            (2, 1), // 2
            (3, 1), // 3
            (2, 2), // 4
            (3, 2), // 5
            (3, 2), // 6
            (4, 2), // 7
            (4, 2), // 8
            (3, 3), // 9
            (5, 2), // 10
            (4, 3), // 11
            (4, 3), // 12
        ];

    [API.APIDescription("Takes an image and stores it directly in the user's history.\nBehaves identical to GenerateText2Image but never queues a generation.",
        """
            "images":
            [
                {
                    "image": "View/local/raw/2024-01-02/0304-a photo of a cat-etc-1.png", // the image file path, GET this path to read the image content
                    "batch_index": "0", // which image index within the batch this is
                    "metadata": "{ ... }" // image metadata string, usually a JSON blob stringified. Not guaranteed to be.
                }
            ]
        """)]
    public static async Task<JObject> AddImageToHistory(Session session,
        [API.APIParameter("Data URL of the image to save.")] string image,
        [API.APIParameter("Raw mapping of input should contain general T2I parameters (see listing on Generate tab of main interface) to values, eg `{ \"prompt\": \"a photo of a cat\", \"model\": \"OfficialStableDiffusion/sd_xl_base_1.0\", \"steps\": 20, ... }`. Note that this is the root raw map, ie all params go on the same level as `images`, `session_id`, etc.")] JObject rawInput)
    {
        // TODO: Recognize audio/video inputs properly
        ImageFile img = ImageFile.FromDataString(image);
        T2IParamInput user_input;
        rawInput.Remove("image");
        try
        {
            user_input = RequestToParams(session, rawInput);
        }
        catch (SwarmReadableErrorException ex)
        {
            return new() { ["error"] = ex.Message };
        }
        // This endpoint doesn't run a generation pipeline, so no params are naturally "queried".
        // Keep full parameter metadata instead of collapsing most fields into `unused_parameters`.
        user_input.NoUnusedParams = true;
        user_input.ApplySpecialLogic();
        Logs.Info($"User {session.User.UserID} stored an image to history.");
        (Task<MediaFile> imgTask, string metadata) = user_input.SourceSession.ApplyMetadata(img, user_input, 1);
        T2IEngine.ImageOutput outputImage = new() { File = img as Image, ActualFileTask = imgTask };
        (string path, _) = session.SaveImage(outputImage, 0, user_input, metadata);
        return new() { ["images"] = new JArray() { new JObject() { ["image"] = path, ["batch_index"] = "0", ["request_id"] = $"{user_input.UserRequestId}", ["metadata"] = metadata } } };
    }

    public static string[] GetIOPaintCommandCandidates()
    {
        return ClassicInpaintAPI.GetIOPaintCommandCandidates();
    }

    public static async Task<(int, string)> RunProcessCapture(string fileName, string[] args, string workingDirectory = null)
    {
        return await ClassicInpaintAPI.RunProcessCapture(fileName, args, workingDirectory);
    }

    public static void PrepareClassicInpaintMask(ISImageRGBA maskImage, int expandMask, int feather)
    {
        ClassicInpaintAPI.PrepareClassicInpaintMask(maskImage, expandMask, feather);
    }

    public static async Task<HashSet<string>> GetSupportedClassicInpaintBackends()
    {
        return await ClassicInpaintAPI.GetSupportedClassicInpaintBackends();
    }

    public static async Task<JObject> GetClassicInpaintBackends(Session session)
    {
        return await ClassicInpaintAPI.GetClassicInpaintBackends(session);
    }

    public static async Task<JObject> ClassicInpaint(Session session, string imageData, string maskData, string backend = "lama", int feather = 8, int expandMask = 4)
    {
        return await ClassicInpaintAPI.ClassicInpaint(session, imageData, maskData, backend, feather, expandMask);
    }

    public static HashSet<string> HistoryExtensions = // TODO: Use MediaType?
    [
        "png", "jpg", // image
        "html", // special
        "gif", "webp", // animation
        "webm", "mp4", "mov", // video
        "mp3", "aac", "wav", "flac" // audio
    ];

    public enum ImageHistorySortMode { Name, Date, DateCreated, DateEdited, Rating, Resolution, Model, Seed, FileSize }

    private static bool MetadataIsHidden(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"is_hidden\""))
        {
            return false;
        }
        try
        {
            JToken hidden = metadata.Metadata.ParseToJson()["is_hidden"];
            return hidden?.Type == JTokenType.Boolean && hidden.Value<bool>();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static double MetadataRating(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"rating\""))
        {
            return 0;
        }
        try
        {
            JToken rating = metadata.Metadata.ParseToJson()["rating"];
            return rating is null ? 0 : rating.Value<double>();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static long MetadataResolutionPixels(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"sui_image_params\""))
        {
            return 0;
        }
        try
        {
            JObject parsed = metadata.Metadata.ParseToJson();
            JObject parameters = parsed["sui_image_params"] as JObject;
            long width = parameters?["width"]?.Value<long>() ?? 0;
            long height = parameters?["height"]?.Value<long>() ?? 0;
            JObject extra = parsed["sui_extra_data"] as JObject;
            width = extra?["final_width"]?.Value<long>() ?? width;
            height = extra?["final_height"]?.Value<long>() ?? height;
            return width * height;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string MetadataModel(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"model\""))
        {
            return "";
        }
        try
        {
            JToken model = metadata.Metadata.ParseToJson()["sui_image_params"]?["model"];
            return $"{model}".ToLowerFast();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static long MetadataSeed(OutputMetadataTracker.OutputMetadataEntry metadata)
    {
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Metadata) || !metadata.Metadata.Contains("\"seed\""))
        {
            return 0;
        }
        try
        {
            JToken seed = metadata.Metadata.ParseToJson()["sui_image_params"]?["seed"];
            return seed is null ? 0 : seed.Value<long>();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Splits an image history filter query into terms while preserving quoted text.</summary>
    private static List<string> SplitImageHistoryFilterQuery(string filter)
    {
        List<string> terms = [];
        string current = "";
        char quote = '\0';
        foreach (char character in filter ?? "")
        {
            if ((character == '"' || character == '\'') && (quote == '\0' || quote == character))
            {
                quote = quote == character ? '\0' : character;
                continue;
            }
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    terms.Add(current.Trim());
                }
                current = "";
                continue;
            }
            current += character;
        }
        if (!string.IsNullOrWhiteSpace(current))
        {
            terms.Add(current.Trim());
        }
        return terms;
    }

    /// <summary>Normalizes aliases for image history structured search fields.</summary>
    private static string NormalizeImageHistoryFilterField(string field)
    {
        return field switch
        {
            "fav" => "favorite",
            "starred" => "favorite",
            "hide" => "hidden",
            "is_hidden" => "hidden",
            "tag" => "tags",
            "neg" => "negative",
            "negativeprompt" => "negative",
            "loras" => "lora",
            "res" => "resolution",
            "file_size" => "filesize",
            "size" => "filesize",
            "final_width" => "finalwidth",
            "final_height" => "finalheight",
            "prompt_lab" => "promptlab",
            "prompt_lab_id" => "promptlab",
            "ext" => "filetype",
            "file_type" => "filetype",
            "has_metadata" => "has",
            "wildcard_values" => "wildcard",
            _ => field
        };
    }

    /// <summary>Compiles image history filter terms for repeated matching.</summary>
    private static List<(string Field, string Value, string Raw)> CompileImageHistoryFilterQuery(string filter)
    {
        List<(string Field, string Value, string Raw)> terms = [];
        foreach (string term in SplitImageHistoryFilterQuery(filter))
        {
            int fieldSplit = term.IndexOf(':');
            if (fieldSplit > 0)
            {
                string field = NormalizeImageHistoryFilterField(term[..fieldSplit].ToLowerFast());
                string value = term[(fieldSplit + 1)..].ToLowerFast();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    terms.Add((field, value, term.ToLowerFast()));
                }
            }
            else
            {
                string value = term.ToLowerFast();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    terms.Add((null, value, value));
                }
            }
        }
        return terms;
    }

    /// <summary>Returns searchable text for a boolean history filter field.</summary>
    private static string ImageHistoryBoolSearchText(bool value, string trueWord, string falseWord)
    {
        return value ? $"true yes {trueWord}" : $"false no {falseWord}";
    }

    /// <summary>Returns searchable text for an indexed history field.</summary>
    private static string GetIndexedHistoryFilterField(OutputMetadataTracker.OutputHistoryIndexEntry entry, string field)
    {
        return field switch
        {
            "name" => entry.RelativePath.AfterLast('/'),
            "path" => entry.RelativePath,
            "folder" => entry.Folder ?? "",
            "type" or "filetype" => entry.Extension ?? "",
            "filesize" => entry.FileSize.ToString(),
            "date" => $"{DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, entry.FileTime)):O} {entry.RelativePath}",
            "metadata" or "prompt" or "negative" or "lora" or "vae" or "sampler" or "scheduler" or "resolution" or "width" or "height" or "finalwidth" or "finalheight" or "steps" or "cfg" or "notes" or "tags" or "session" or "wildcard" or "promptlab" => entry.Metadata ?? "",
            "model" => entry.Model ?? "",
            "seed" => entry.Seed.ToString(),
            "rating" => entry.Rating.ToString(),
            "hidden" => ImageHistoryBoolSearchText(entry.IsHidden, "hidden", "visible"),
            "favorite" => ImageHistoryBoolSearchText(entry.IsStarred || entry.RelativePath.StartsWith("Starred/"), "starred favorite", "unstarred"),
            "has" => ImageHistoryBoolSearchText(!string.IsNullOrWhiteSpace(entry.Metadata), "metadata", "none"),
            _ => null
        };
    }

    /// <summary>Returns searchable text for a scanned history field.</summary>
    private static string GetScannedHistoryFilterField(ImageHistoryHelper entry, string field)
    {
        bool isStarred = entry.Name.StartsWith("Starred/");
        bool isHidden = MetadataIsHidden(entry.Metadata);
        return field switch
        {
            "name" => entry.Name.AfterLast('/'),
            "path" => entry.Name,
            "folder" => entry.Name.Contains('/') ? entry.Name.BeforeLast('/') : "",
            "type" or "filetype" => entry.Name.AfterLast('.').ToLowerFast(),
            "filesize" => entry.FileSize.ToString(),
            "date" => $"{DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, entry.Metadata?.FileTime ?? 0)):O} {entry.Name}",
            "metadata" or "prompt" or "negative" or "lora" or "vae" or "sampler" or "scheduler" or "resolution" or "width" or "height" or "finalwidth" or "finalheight" or "steps" or "cfg" or "notes" or "tags" or "session" or "wildcard" or "promptlab" => entry.Metadata?.Metadata ?? "",
            "model" => MetadataModel(entry.Metadata),
            "seed" => MetadataSeed(entry.Metadata).ToString(),
            "rating" => MetadataRating(entry.Metadata).ToString(),
            "hidden" => ImageHistoryBoolSearchText(isHidden, "hidden", "visible"),
            "favorite" => ImageHistoryBoolSearchText(isStarred, "starred favorite", "unstarred"),
            "has" => ImageHistoryBoolSearchText(!string.IsNullOrWhiteSpace(entry.Metadata?.Metadata), "metadata", "none"),
            _ => null
        };
    }

    /// <summary>Checks a numeric comparison term against a field value, or null when the term is not numeric.</summary>
    private static bool? ImageHistoryNumericFilterMatches(string fieldText, string value)
    {
        string op = value.StartsWith(">=") || value.StartsWith("<=") ? value[..2] : value.Length > 0 && "<>=".Contains(value[0]) ? value[..1] : null;
        if (op is null || !double.TryParse(value[op.Length..], out double expected) || !double.TryParse(fieldText, out double actual))
        {
            return null;
        }
        return op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            _ => actual == expected
        };
    }

    /// <summary>Checks a date comparison term against a field value, or null when the term is not a date comparison.</summary>
    private static bool? ImageHistoryDateFilterMatches(string fieldText, string value)
    {
        string op = value.StartsWith(">=") || value.StartsWith("<=") ? value[..2] : value.Length > 0 && "<>=".Contains(value[0]) ? value[..1] : null;
        if (op is null || !DateTimeOffset.TryParse(value[op.Length..], out DateTimeOffset expected))
        {
            return null;
        }
        int split = fieldText.IndexOf(' ');
        string actualText = split < 0 ? fieldText : fieldText[..split];
        if (!DateTimeOffset.TryParse(actualText, out DateTimeOffset actual))
        {
            return false;
        }
        return op switch
        {
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            _ => actual == expected
        };
    }

    /// <summary>Checks whether searchable history text matches compiled filter terms.</summary>
    private static bool ImageHistoryFilterMatches(string allFields, Func<string, string> getField, List<(string Field, string Value, string Raw)> filterTerms)
    {
        if (filterTerms.Count == 0)
        {
            return true;
        }
        allFields = (allFields ?? "").ToLowerFast();
        foreach ((string field, string value, string raw) in filterTerms)
        {
            if (field is null)
            {
                if (!allFields.Contains(value))
                {
                    return false;
                }
                continue;
            }
            string fieldText = getField(field);
            if (fieldText is null)
            {
                if (!allFields.Contains(raw))
                {
                    return false;
                }
                continue;
            }
            fieldText = fieldText.ToLowerFast();
            bool? numericMatch = ImageHistoryNumericFilterMatches(fieldText, value);
            if (numericMatch is not null)
            {
                if (numericMatch != true)
                {
                    return false;
                }
                continue;
            }
            bool? dateMatch = ImageHistoryDateFilterMatches(fieldText, value);
            if (dateMatch is not null)
            {
                if (dateMatch != true)
                {
                    return false;
                }
                continue;
            }
            if (!fieldText.Contains(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Checks whether an indexed history entry matches compiled filter terms.</summary>
    private static bool IndexedImageHistoryFilterMatches(OutputMetadataTracker.OutputHistoryIndexEntry entry, List<(string Field, string Value, string Raw)> filterTerms)
    {
        string allFields = $"{entry.RelativePath} {entry.Extension} {entry.Metadata} {entry.Model} {entry.Seed} {entry.Rating}";
        return ImageHistoryFilterMatches(allFields, field => GetIndexedHistoryFilterField(entry, field), filterTerms);
    }

    /// <summary>Checks whether a scanned history entry matches compiled filter terms.</summary>
    private static bool ScannedImageHistoryFilterMatches(ImageHistoryHelper entry, List<(string Field, string Value, string Raw)> filterTerms)
    {
        string allFields = $"{entry.Name} {entry.Metadata?.Metadata} {MetadataModel(entry.Metadata)} {MetadataSeed(entry.Metadata)} {MetadataRating(entry.Metadata)}";
        return ImageHistoryFilterMatches(allFields, field => GetScannedHistoryFilterField(entry, field), filterTerms);
    }

    private static ImageHistoryHelper GetImageHistoryHelper(string name, string file, string root, bool starNoFolders)
    {
        OutputMetadataTracker.OutputMetadataEntry metadata = OutputMetadataTracker.GetMetadataFor(file, root, starNoFolders);
        long fileSize = 0;
        long fileCreatedTime = 0;
        try
        {
            FileInfo info = new(file);
            fileSize = info.Length;
            fileCreatedTime = ((DateTimeOffset)info.CreationTimeUtc).ToUnixTimeSeconds();
        }
        catch (Exception)
        {
        }
        return new(name, metadata, fileSize, fileCreatedTime);
    }

    /// <summary>Sorts history index entries with the requested sort mode.</summary>
    private static List<OutputMetadataTracker.OutputHistoryIndexEntry> SortHistoryIndexEntries(List<OutputMetadataTracker.OutputHistoryIndexEntry> entries, ImageHistorySortMode sortBy, bool sortReverse)
    {
        if (sortBy == ImageHistorySortMode.Name)
        {
            entries.Sort((a, b) => b.RelativePath.CompareTo(a.RelativePath));
        }
        else if (sortBy == ImageHistorySortMode.Date || sortBy == ImageHistorySortMode.DateEdited)
        {
            entries.Sort((a, b) => b.FileTime.CompareTo(a.FileTime));
        }
        else if (sortBy == ImageHistorySortMode.DateCreated)
        {
            entries.Sort((a, b) => b.FileCreatedTime.CompareTo(a.FileCreatedTime));
        }
        else if (sortBy == ImageHistorySortMode.Rating)
        {
            entries.Sort((a, b) => b.Rating.CompareTo(a.Rating));
        }
        else if (sortBy == ImageHistorySortMode.Resolution)
        {
            entries.Sort((a, b) => b.ResolutionPixels.CompareTo(a.ResolutionPixels));
        }
        else if (sortBy == ImageHistorySortMode.Model)
        {
            entries.Sort((a, b) => (b.Model ?? "").CompareTo(a.Model ?? ""));
        }
        else if (sortBy == ImageHistorySortMode.Seed)
        {
            entries.Sort((a, b) => b.Seed.CompareTo(a.Seed));
        }
        else if (sortBy == ImageHistorySortMode.FileSize)
        {
            entries.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
        }
        if (sortReverse)
        {
            entries.Reverse();
        }
        return entries;
    }

    /// <summary>Attempts to answer a history list request from the persistent history index.</summary>
    private static JObject TryGetListFromHistoryIndex(Session session, string rawRefPath, int depth, ImageHistorySortMode sortBy, bool sortReverse, bool includeHidden, bool fastFirst, int fastFirstLimit, int maxInHistory, List<(string Field, string Value, string Raw)> filterTerms, long timeStart)
    {
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        int limit = fastFirst ? Math.Max(1, Math.Min(fastFirstLimit, maxInHistory)) : maxInHistory;
        string requestPrefix = rawRefPath == "./" ? "" : rawRefPath;
        requestPrefix = requestPrefix.Replace('\\', '/').Trim('/');
        if (!OutputMetadataTracker.IsHistoryIndexPrefixComplete(root, requestPrefix))
        {
            return null;
        }
        List<OutputMetadataTracker.OutputHistoryIndexEntry> indexed = OutputMetadataTracker.GetHistoryIndexForPrefix(root, requestPrefix);
        if (indexed.Count == 0)
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(requestPrefix))
        {
            requestPrefix += "/";
        }
        HashSet<string> folders = [];
        List<OutputMetadataTracker.OutputHistoryIndexEntry> files = [];
        string specialPrefix = requestPrefix == "" ? "" : $"{requestPrefix}/";
        foreach (string specialFolder in UserImageHistoryHelper.SharedSpecialFolders.Keys)
        {
            if (specialFolder.StartsWith(specialPrefix))
            {
                string relativeSpecialFolder = specialFolder[specialPrefix.Length..];
                relativeSpecialFolder = relativeSpecialFolder.EndsWith('/') ? relativeSpecialFolder[..^1] : relativeSpecialFolder;
                if (!string.IsNullOrWhiteSpace(relativeSpecialFolder))
                {
                    folders.Add(relativeSpecialFolder);
                }
            }
        }
        foreach (OutputMetadataTracker.OutputHistoryIndexEntry entry in indexed)
        {
            if (!includeHidden && entry.IsHidden)
            {
                continue;
            }
            if (filterTerms.Count > 0 && !IndexedImageHistoryFilterMatches(entry, filterTerms))
            {
                continue;
            }
            if (!entry.RelativePath.StartsWith(requestPrefix))
            {
                continue;
            }
            string relative = entry.RelativePath[requestPrefix.Length..];
            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/'))
            {
                continue;
            }
            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            int folderDepth = parts.Length - 1;
            int folderLimit = Math.Min(folderDepth, depth);
            string folderPath = "";
            for (int i = 0; i < folderLimit; i++)
            {
                folderPath = folderPath == "" ? parts[i] : $"{folderPath}/{parts[i]}";
                folders.Add(folderPath);
            }
            if (folderDepth <= depth)
            {
                files.Add(entry);
            }
        }
        if (files.Count == 0 && folders.Count == 0)
        {
            return null;
        }
        HashSet<string> included = [.. files.Select(f => f.RelativePath)];
        files = [.. files.Where(f =>
        {
            if (f.RelativePath.StartsWith("Starred/"))
            {
                return true;
            }
            string starPath = session.User.Settings.StarNoFolders ? $"Starred/{f.RelativePath.Replace("/", "")}" : $"Starred/{f.RelativePath}";
            return !included.Contains(starPath);
        })];
        if (sortBy == ImageHistorySortMode.DateCreated && files.Any(f => f.FileCreatedTime <= 0))
        {
            return null;
        }
        files = SortHistoryIndexEntries(files, sortBy, sortReverse);
        long timeEnd = Environment.TickCount64;
        Logs.Verbose($"Listed {files.Count} indexed images from {folders.Count} indexed folder entries in {(timeEnd - timeStart) / 1000.0:0.###} seconds.");
        return new JObject()
        {
            ["folders"] = JToken.FromObject(folders.OrderDescending().ToList()),
            ["files"] = JToken.FromObject(files.Take(limit).Select(f =>
            {
                string src = requestPrefix == "" ? f.RelativePath : f.RelativePath[requestPrefix.Length..];
                return new JObject() { ["src"] = src, ["metadata"] = f.Metadata, ["file_size"] = f.FileSize, ["file_time"] = f.FileTime, ["file_created_time"] = f.FileCreatedTime };
            }).ToList()),
            ["perf"] = new JObject()
            {
                ["total_ms"] = timeEnd - timeStart,
                ["dir_scan_ms"] = 0,
                ["file_scan_ms"] = 0,
                ["final_sort_ms"] = 0,
                ["fast_first"] = fastFirst,
                ["indexed"] = true
            }
        };
    }

    /// <summary>Removes a file path from the persistent history index.</summary>
    private static void RemoveHistoryIndexForPath(string root, string path)
    {
        OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, OutputMetadataTracker.GetRelativePath(path, root));
    }

    /// <summary>Refreshes the persistent history index entry for a file path.</summary>
    private static void RefreshHistoryIndexForPath(string root, string path, bool starNoFolders)
    {
        RemoveHistoryIndexForPath(root, path);
        OutputMetadataTracker.UpsertHistoryIndexForFile(path.Replace('\\', '/'), root, starNoFolders);
    }

    private static JObject GetListAPIInternal(Session session, string rawPath, string root, HashSet<string> extensions, Func<string, bool> isAllowed, int depth, ImageHistorySortMode sortBy, bool sortReverse, bool includeHidden, bool fastFirst = false, int fastFirstLimit = 128, bool forceScan = false, string filter = "")
    {
        int maxInHistory = session.User.Settings.MaxImagesInHistory;
        int maxScanned = session.User.Settings.MaxImagesScannedInHistory;
        List<(string Field, string Value, string Raw)> filterTerms = CompileImageHistoryFilterQuery(filter);
        Logs.Verbose($"User {session.User.UserID} wants to list images in '{rawPath}', maxDepth={depth}, sortBy={sortBy}, reverse={sortReverse}, includeHidden={includeHidden}, fastFirst={fastFirst}, fastFirstLimit={fastFirstLimit}, maxInHistory={maxInHistory}, maxScanned={maxScanned}, filterTerms={filterTerms.Count}");
        long timeStart = Environment.TickCount64;
        long dirScanMs = 0;
        long fileScanMs = 0;
        long finalSortMs = 0;
        int limit = sortBy == ImageHistorySortMode.Name && filterTerms.Count == 0 ? maxInHistory : Math.Max(maxInHistory, maxScanned);
        (string path, string consoleError, string userError) = WebServer.CheckFilePath(root, rawPath);
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        try
        {
            ConcurrentDictionary<string, string> finalDirs = [];
            bool starNoFolders = session.User.Settings.StarNoFolders;
            string rawRefPath = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!rawRefPath.EndsWith('/'))
            {
                rawRefPath += '/';
            }
            if (rawRefPath == "./")
            {
                rawRefPath = "";
            }
            if (forceScan)
            {
                OutputMetadataTracker.RemoveHistoryIndexPrefixComplete(root, rawRefPath);
                OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, rawRefPath);
            }
            TryStartImageHistoryIndexWarmup(session, root, path, rawRefPath);
            JObject indexedResult = forceScan ? null : TryGetListFromHistoryIndex(session, rawRefPath, depth, sortBy, sortReverse, includeHidden, fastFirst, fastFirstLimit, maxInHistory, filterTerms, timeStart);
            if (indexedResult is not null)
            {
                return indexedResult;
            }
            List<string> specialSeedDirs = [.. UserImageHistoryHelper.SharedSpecialFolders.Keys
                .Where(f => f.StartsWith(rawRefPath))
                .Select(f => f[rawRefPath.Length..])
                .Select(f => f.EndsWith('/') ? f[..^1] : f)
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .OrderDescending()];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, double> ratingSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, long> resolutionSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, string> modelSortCache = [];
            Dictionary<OutputMetadataTracker.OutputMetadataEntry, long> seedSortCache = [];
            double cachedRating(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!ratingSortCache.TryGetValue(metadata, out double result))
                {
                    result = MetadataRating(metadata);
                    ratingSortCache[metadata] = result;
                }
                return result;
            }
            long cachedResolution(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!resolutionSortCache.TryGetValue(metadata, out long result))
                {
                    result = MetadataResolutionPixels(metadata);
                    resolutionSortCache[metadata] = result;
                }
                return result;
            }
            string cachedModel(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return "";
                }
                if (!modelSortCache.TryGetValue(metadata, out string result))
                {
                    result = MetadataModel(metadata);
                    modelSortCache[metadata] = result;
                }
                return result;
            }
            long cachedSeed(OutputMetadataTracker.OutputMetadataEntry metadata)
            {
                if (metadata is null)
                {
                    return 0;
                }
                if (!seedSortCache.TryGetValue(metadata, out long result))
                {
                    result = MetadataSeed(metadata);
                    seedSortCache[metadata] = result;
                }
                return result;
            }
            void sortList(List<ImageHistoryHelper> list)
            {
                if (sortBy == ImageHistorySortMode.Name)
                {
                    list.Sort((a, b) => b.Name.CompareTo(a.Name));
                }
                else if (sortBy == ImageHistorySortMode.Date || sortBy == ImageHistorySortMode.DateEdited)
                {
                    list.Sort((a, b) => b.Metadata.FileTime.CompareTo(a.Metadata.FileTime));
                }
                else if (sortBy == ImageHistorySortMode.DateCreated)
                {
                    list.Sort((a, b) => b.FileCreatedTime.CompareTo(a.FileCreatedTime));
                }
                else if (sortBy == ImageHistorySortMode.Rating)
                {
                    list.Sort((a, b) => cachedRating(b.Metadata).CompareTo(cachedRating(a.Metadata)));
                }
                else if (sortBy == ImageHistorySortMode.Resolution)
                {
                    list.Sort((a, b) => cachedResolution(b.Metadata).CompareTo(cachedResolution(a.Metadata)));
                }
                else if (sortBy == ImageHistorySortMode.Model)
                {
                    list.Sort((a, b) => cachedModel(b.Metadata).CompareTo(cachedModel(a.Metadata)));
                }
                else if (sortBy == ImageHistorySortMode.Seed)
                {
                    list.Sort((a, b) => cachedSeed(b.Metadata).CompareTo(cachedSeed(a.Metadata)));
                }
                else if (sortBy == ImageHistorySortMode.FileSize)
                {
                    list.Sort((a, b) => b.FileSize.CompareTo(a.FileSize));
                }
                if (sortReverse)
                {
                    list.Reverse();
                }
            }
            List<string> dirs;
            List<ImageHistoryHelper> files;
            if (fastFirst)
            {
                long fastStart = Environment.TickCount64;
                int startupLimit = Math.Max(1, Math.Min(fastFirstLimit, maxInHistory));
                HashSet<string> seenDirs = [];
                HashSet<string> traversedDirs = [];
                dirs = [];
                files = [];
                void collectFastFirstFiles(string dir, int subDepth)
                {
                    if (!traversedDirs.Add(dir))
                    {
                        return;
                    }
                    if (files.Count >= startupLimit)
                    {
                        return;
                    }
                    string prefix = dir == "" ? "" : dir + "/";
                    string actualPath = $"{path}/{prefix}";
                    actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                    if (!Directory.Exists(actualPath))
                    {
                        return;
                    }
                    IEnumerable<string> orderedFiles = Directory.EnumerateFiles(actualPath)
                        .Select(f => f.Replace('\\', '/'))
                        .Where(isAllowed)
                        .Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"))
                        .OrderDescending()
                        .Take(startupLimit - files.Count);
                    files.AddRange(orderedFiles
                        .Select(f => GetImageHistoryHelper(prefix + f.AfterLast('/'), f, root, starNoFolders))
                        .Where(f => f.Metadata is not null)
                        .Where(f => includeHidden || !MetadataIsHidden(f.Metadata))
                        .Where(f => filterTerms.Count == 0 || ScannedImageHistoryFilterMatches(f, filterTerms)));
                    if (files.Count >= startupLimit || subDepth <= 0)
                    {
                        return;
                    }
                    IEnumerable<string> subDirs;
                    if (dir == "")
                    {
                        subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).Concat(specialSeedDirs).Distinct().OrderDescending();
                    }
                    else
                    {
                        subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).OrderDescending();
                    }
                    foreach (string subDir in subDirs)
                    {
                        if (subDir.StartsWithFast('.'))
                        {
                            continue;
                        }
                        string subPath = dir == "" ? subDir : $"{dir}/{subDir}";
                        if (!isAllowed(subPath))
                        {
                            continue;
                        }
                        if (seenDirs.Add(subPath))
                        {
                            dirs.Add(subPath);
                        }
                        collectFastFirstFiles(subPath, subDepth - 1);
                        if (files.Count >= startupLimit)
                        {
                            return;
                        }
                    }
                }
                collectFastFirstFiles("", depth);
                fileScanMs = Environment.TickCount64 - fastStart;
            }
            else
            {
                long dirStart = Environment.TickCount64;
                ConcurrentDictionary<string, string> dirsConc = [];
                HashSet<string> traversedDirs = [];
                void addDirs(string dir, int subDepth)
                {
                    if (dir.EndsWith('/'))
                    {
                        dir = dir[..^1];
                    }
                    if (!traversedDirs.Add(dir))
                    {
                        return;
                    }
                    if (dir != "")
                    {
                        (subDepth == 0 ? finalDirs : dirsConc).TryAdd(dir, dir);
                    }
                    if (subDepth > 0)
                    {
                        string actualPath = $"{path}/{dir}";
                        actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                        if (!Directory.Exists(actualPath))
                        {
                            return;
                        }
                        IEnumerable<string> subDirs = Directory.EnumerateDirectories(actualPath).Select(Path.GetFileName).OrderDescending();
                        foreach (string subDir in subDirs)
                        {
                            if (subDir.StartsWithFast('.'))
                            {
                                continue;
                            }
                            string subPath = dir == "" ? subDir : $"{dir}/{subDir}";
                            if (isAllowed(subPath))
                            {
                                addDirs(subPath, subDepth - 1);
                            }
                        }
                    }
                }
                addDirs("", depth);
                foreach (string specialFolder in UserImageHistoryHelper.SharedSpecialFolders.Keys)
                {
                    if (specialFolder.StartsWith(rawRefPath))
                    {
                        addDirs(specialFolder[rawRefPath.Length..], 1);
                    }
                }
                dirs = [.. dirsConc.Keys.OrderDescending()];
                if (sortReverse)
                {
                    dirs.Reverse();
                }
                dirScanMs = Environment.TickCount64 - dirStart;
                long fileStart = Environment.TickCount64;
                ConcurrentDictionary<int, List<ImageHistoryHelper>> filesConc = [];
                int id = 0;
                int remaining = limit;
                int remainingScans = filterTerms.Count == 0 ? 0 : maxScanned;
                Parallel.ForEach(dirs.Append(""), new ParallelOptions() { MaxDegreeOfParallelism = 5, CancellationToken = Program.GlobalProgramCancel }, folder =>
                {
                    int localId = Interlocked.Increment(ref id);
                    int localLimit = filterTerms.Count == 0 ? Interlocked.CompareExchange(ref remaining, 0, 0) : Interlocked.CompareExchange(ref remainingScans, 0, 0);
                    if (localLimit <= 0)
                    {
                        return;
                    }
                    string prefix = folder == "" ? "" : folder + "/";
                    string actualPath = $"{path}/{prefix}";
                    actualPath = UserImageHistoryHelper.GetRealPathFor(session.User, actualPath, root: root);
                    if (!Directory.Exists(actualPath))
                    {
                        return;
                    }
                    List<string> subFiles = [.. Directory.EnumerateFiles(actualPath).Take(localLimit)];
                    IEnumerable<string> newFileNames = subFiles.Select(f => f.Replace('\\', '/')).Where(isAllowed).Where(f => !f.AfterLast('/').StartsWithFast('.') && extensions.Contains(f.AfterLast('.')) && !f.EndsWith(".swarmpreview.jpg") && !f.EndsWith(".swarmpreview.webp"));
                    List<ImageHistoryHelper> localFiles = [.. newFileNames.Select(f => GetImageHistoryHelper(prefix + f.AfterLast('/'), f, root, starNoFolders)).Where(f => f.Metadata is not null).Where(f => includeHidden || !MetadataIsHidden(f.Metadata)).Where(f => filterTerms.Count == 0 || ScannedImageHistoryFilterMatches(f, filterTerms))];
                    int leftOver = filterTerms.Count == 0 ? Interlocked.Add(ref remaining, -localFiles.Count) : Interlocked.Add(ref remainingScans, -subFiles.Count);
                    sortList(localFiles);
                    filesConc.TryAdd(localId, localFiles);
                    if (leftOver <= 0)
                    {
                        return;
                    }
                });
                files = [.. filesConc.Values.SelectMany(f => f).Take(limit)];
                fileScanMs = Environment.TickCount64 - fileStart;
            }
            long finalSortStart = Environment.TickCount64;
            HashSet<string> included = [.. files.Select(f => f.Name)];
            for (int i = 0; i < files.Count; i++)
            {
                if (!files[i].Name.StartsWith("Starred/"))
                {
                    string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? files[i].Name.Replace("/", "") : files[i].Name)}";
                    if (included.Contains(starPath))
                    {
                        files[i] = files[i] with { Name = null };
                    }
                }
            }
            files = [.. files.Where(f => f.Name is not null)];
            sortList(files);
            finalSortMs = Environment.TickCount64 - finalSortStart;
            long timeEnd = Environment.TickCount64;
            Logs.Verbose($"Listed {files.Count} images from {dirs.Count} folder entries in {(timeEnd - timeStart) / 1000.0:0.###} seconds (dirScan={dirScanMs / 1000.0:0.###}s, fileScan={fileScanMs / 1000.0:0.###}s, finalSort={finalSortMs / 1000.0:0.###}s, fastFirst={fastFirst}).");
            return new JObject()
            {
                ["folders"] = JToken.FromObject(dirs.Union(finalDirs.Keys).ToList()),
                ["files"] = JToken.FromObject(files.Take(maxInHistory).Select(f => new JObject() { ["src"] = f.Name, ["metadata"] = f.Metadata.Metadata, ["file_size"] = f.FileSize, ["file_time"] = f.Metadata.FileTime, ["file_created_time"] = f.FileCreatedTime }).ToList()),
                ["perf"] = new JObject()
                {
                    ["total_ms"] = timeEnd - timeStart,
                    ["dir_scan_ms"] = dirScanMs,
                    ["file_scan_ms"] = fileScanMs,
                    ["final_sort_ms"] = finalSortMs,
                    ["fast_first"] = fastFirst
                }
            };
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is PathTooLongException)
            {
                return new JObject() { ["error"] = "404, path not found." };
            }
            else
            {
                Logs.Error($"Error reading file list: {ex.ReadableString()}");
                return new JObject() { ["error"] = "Error reading file list." };
            }
        }
    }

    public record struct ImageHistoryHelper(string Name, OutputMetadataTracker.OutputMetadataEntry Metadata, long FileSize, long FileCreatedTime);

    /// <summary>Currently running background history index warm-up jobs.</summary>
    public static ConcurrentDictionary<string, byte> ImageHistoryIndexWarmups = [];

    /// <summary>Returns whether a relative image history path is safe to index under the output root.</summary>
    private static bool CanIndexHistoryPrefix(string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        return relativePrefix != ".." && !relativePrefix.StartsWith("../");
    }

    /// <summary>Indexes one image history folder.</summary>
    private static (int Indexed, int Skipped) IndexImageHistoryFolder(string root, string checkedPath, bool starNoFolders, bool clearIndex, bool rebuildMetadata)
    {
        int indexed = 0;
        int skipped = 0;
        string relativePrefix = OutputMetadataTracker.GetRelativePath(checkedPath, root);
        if (!CanIndexHistoryPrefix(relativePrefix))
        {
            return (indexed, skipped);
        }
        if (clearIndex)
        {
            OutputMetadataTracker.RemoveHistoryIndexPrefixComplete(root, relativePrefix);
            OutputMetadataTracker.RemoveHistoryIndexForPrefix(root, relativePrefix);
        }
        foreach (string rawFile in Directory.EnumerateFiles(checkedPath, "*", SearchOption.AllDirectories))
        {
            string file = rawFile.Replace('\\', '/');
            string filename = file.AfterLast('/');
            string ext = file.AfterLast('.').ToLowerFast();
            if (filename.StartsWithFast('.') || !HistoryExtensions.Contains(ext) || file.EndsWith(".swarmpreview.jpg") || file.EndsWith(".swarmpreview.webp"))
            {
                skipped++;
                continue;
            }
            if (rebuildMetadata)
            {
                OutputMetadataTracker.RemoveMetadataFor(file);
            }
            OutputMetadataTracker.OutputHistoryIndexEntry entry = OutputMetadataTracker.UpsertHistoryIndexForFile(file, root, starNoFolders);
            if (entry is null)
            {
                skipped++;
                continue;
            }
            indexed++;
        }
        OutputMetadataTracker.MarkHistoryIndexPrefixComplete(root, relativePrefix);
        return (indexed, skipped);
    }

    /// <summary>Starts a background history index warm-up for a folder if needed.</summary>
    private static void TryStartImageHistoryIndexWarmup(Session session, string root, string checkedPath, string relativePrefix)
    {
        relativePrefix = (relativePrefix ?? "").Replace('\\', '/').Trim('/');
        if (!CanIndexHistoryPrefix(relativePrefix) || OutputMetadataTracker.IsHistoryIndexPrefixComplete(root, relativePrefix))
        {
            return;
        }
        string warmupKey = $"{root}|{relativePrefix}";
        if (!ImageHistoryIndexWarmups.TryAdd(warmupKey, 1))
        {
            return;
        }
        string userId = session.User.UserID;
        bool starNoFolders = session.User.Settings.StarNoFolders;
        _ = Utilities.RunCheckedTask(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), Program.GlobalProgramCancel);
                if (!Directory.Exists(checkedPath))
                {
                    return;
                }
                long timeStart = Environment.TickCount64;
                (int indexed, int skipped) = IndexImageHistoryFolder(root, checkedPath, starNoFolders, true, false);
                long timeEnd = Environment.TickCount64;
                Logs.Verbose($"Warmed image history index for user '{userId}' path '{relativePrefix}' with {indexed} indexed, {skipped} skipped in {(timeEnd - timeStart) / 1000.0:0.###} seconds.");
            }
            finally
            {
                ImageHistoryIndexWarmups.TryRemove(warmupKey, out _);
            }
        }, "image history index warm-up");
    }

    [API.APIDescription("Gets a list of images in a saved image history folder.",
        """
            "folders": ["Folder1", "Folder2"],
            "files":
            [
                {
                    "src": "path/to/image.jpg",
                    "metadata": "some-metadata" // usually a JSON blob encoded as a string. Not guaranteed.
                }
            ]
        """)]
    public static async Task<JObject> ListImages(Session session,
        [API.APIParameter("The folder path to start the listing in. Use an empty string for root.")] string path,
        [API.APIParameter("Maximum depth (number of recursive folders) to search.")] int depth,
        [API.APIParameter("What to sort the list by - `Name`, `DateCreated`, `DateEdited`, `Date`, `Rating`, `Resolution`, `Model`, `Seed`, or `FileSize`. `Date` is accepted as an alias for `DateEdited`.")] string sortBy = "Name",
        [API.APIParameter("If true, the sorting should be done in reverse.")] bool sortReverse = false,
        [API.APIParameter("If true, include images marked as hidden.")] bool includeHidden = false,
        [API.APIParameter("If true, return only a bounded startup slice biased toward newest work.")] bool fastFirst = false,
        [API.APIParameter("Maximum number of files to return when fastFirst is enabled.")] int fastFirstLimit = 128,
        [API.APIParameter("If true, bypass the persistent history index and scan the folder directly.")] bool forceScan = false,
        [API.APIParameter("Optional text or field:value filter to apply server-side before limiting results.")] string filter = "")
    {
        if (!Enum.TryParse(sortBy, true, out ImageHistorySortMode sortMode))
        {
            return new JObject() { ["error"] = $"Invalid sort mode '{sortBy}'." };
        }
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        return GetListAPIInternal(session, path, root, HistoryExtensions, f => true, depth, sortMode, sortReverse, includeHidden, fastFirst, fastFirstLimit, forceScan, filter);
    }

    [API.APIDescription("Rescan cached image history metadata for a folder.", "{ \"success\": true, \"indexed\": 10, \"skipped\": 0 }")]
    public static async Task<JObject> RescanImageMetadata(Session session,
        [API.APIParameter("The folder path to rescan. Use an empty string for root.")] string path,
        [API.APIParameter("If true, clear cached metadata for each file before rereading.")] bool rebuild)
    {
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (string checkedPath, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        checkedPath = UserImageHistoryHelper.GetRealPathFor(session.User, checkedPath, root: root);
        if (!Directory.Exists(checkedPath))
        {
            return new JObject() { ["error"] = "That folder does not exist." };
        }
        try
        {
            (int indexed, int skipped) = IndexImageHistoryFolder(root, checkedPath, session.User.Settings.StarNoFolders, rebuild, rebuild);
            return new JObject() { ["success"] = true, ["indexed"] = indexed, ["skipped"] = skipped };
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error rescanning image history metadata for '{path}': {ex.ReadableString()}");
            return new JObject() { ["error"] = "Error rescanning image history metadata." };
        }
    }

    [API.APIDescription("Open an image folder in the file explorer. Used for local users directly.", "\"success\": true")]
    public static async Task<JObject> OpenImageFolder(Session session,
        [API.APIParameter("The path to the image to show in the image folder.")] string path)
    {
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (!File.Exists(path))
        {
            Logs.Warning($"User {session.User.UserID} tried to open image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot open." };
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!(IsKdeDesktop() && TrySelectFileInDolphin(path)) && !TryShowFileInLinuxFileManager(path))
            {
                ProcessStartInfo info = new("xdg-open")
                {
                    UseShellExecute = false
                };
                info.ArgumentList.Add(Path.GetDirectoryName(Path.GetFullPath(path)));
                Process.Start(info);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", $"-R \"{Path.GetFullPath(path)}\"");
        }
        else
        {
            Logs.Warning("Cannot open image path on unrecognized OS type.");
            return new JObject() { ["error"] = "Cannot open image folder on this OS." };
        }
        return new JObject() { ["success"] = true };
    }

    /// <summary>Returns whether the current desktop environment is KDE/Plasma.</summary>
    private static bool IsKdeDesktop()
    {
        string currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        string desktopSession = Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? "";
        return currentDesktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) || desktopSession.Contains("kde", StringComparison.OrdinalIgnoreCase) || desktopSession.Contains("plasma", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Attempts to select a file in KDE Dolphin, returning false if unsupported.</summary>
    private static bool TrySelectFileInDolphin(string path)
    {
        try
        {
            ProcessStartInfo info = new("dolphin")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add("--select");
            info.ArgumentList.Add(Path.GetFullPath(path));
            Process process = Process.Start(info);
            if (process is null)
            {
                return false;
            }
            return !process.WaitForExit(1000) || process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logs.Verbose($"Could not select file in Dolphin: {ex.ReadableString()}");
            return false;
        }
    }

    /// <summary>Attempts to select a file in the Linux file manager, returning false if unsupported.</summary>
    private static bool TryShowFileInLinuxFileManager(string path)
    {
        try
        {
            string fileUri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            ProcessStartInfo info = new("dbus-send")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add("--session");
            info.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
            info.ArgumentList.Add("--type=method_call");
            info.ArgumentList.Add("/org/freedesktop/FileManager1");
            info.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
            info.ArgumentList.Add($"array:string:{fileUri}");
            info.ArgumentList.Add("string:");
            Process process = Process.Start(info);
            if (process is null)
            {
                return false;
            }
            return process.WaitForExit(1000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logs.Verbose($"Could not select file in Linux file manager: {ex.ReadableString()}");
            return false;
        }
    }

    public static string[] DeletableFileExtensions = [".txt", ".metadata.js", ".swarm.json", OutputMetadataTracker.HiddenMarkerExtension, ".swarmpreview.jpg", ".swarmpreview.webp"];

    [API.APIDescription("Delete an image from history.", "\"success\": true")]
    public static async Task<JObject> DeleteImage(Session session,
        [API.APIParameter("The path to the image to delete.")] string path)
    {
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        if (!File.Exists(path))
        {
            Logs.Warning($"User {session.User.UserID} tried to delete image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot delete." };
        }
        string standardizedPath = Path.GetFullPath(path);
        Session.RecentlyBlockedFilenames[standardizedPath] = standardizedPath;
        Action<string> deleteFile = Program.ServerSettings.Paths.RecycleDeletedImages ? Utilities.SendFileToRecycle : File.Delete;
        deleteFile(path);
        string fileBase = path.BeforeLast('.');
        foreach (string str in DeletableFileExtensions)
        {
            string altFile = $"{fileBase}{str}";
            if (File.Exists(altFile))
            {
                deleteFile(altFile);
            }
        }
        OutputMetadataTracker.RemoveMetadataFor(path);
        RemoveHistoryIndexForPath(root, path);
        return new JObject() { ["success"] = true };
    }

    [API.APIDescription("Copy or move selected images to another output subfolder.", "\"success\": true")]
    public static async Task<JObject> BulkMoveImages(Session session,
        [API.APIParameter("Image paths to copy or move.")] string[] paths,
        [API.APIParameter("Output subfolder to copy or move into.")] string targetFolder,
        [API.APIParameter("Either copy or move.")] string mode)
    {
        bool copyMode = mode == "copy";
        bool moveMode = mode == "move";
        if (!copyMode && !moveMode)
        {
            return new JObject() { ["error"] = "Mode must be copy or move." };
        }
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        targetFolder = (targetFolder ?? "").Replace('\\', '/').Trim('/');
        (string targetRoot, string consoleError, string userError) = WebServer.CheckFilePath(root, targetFolder);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        targetRoot = UserImageHistoryHelper.GetRealPathFor(session.User, targetRoot, root: root);
        Directory.CreateDirectory(targetRoot);
        int changed = 0;
        int failed = 0;
        foreach (string pathToken in paths ?? [])
        {
            string rawPath = pathToken.Replace('\\', '/').Trim('/');
            (string sourcePath, string pathConsoleError, _) = WebServer.CheckFilePath(root, rawPath);
            if (pathConsoleError is not null)
            {
                failed++;
                continue;
            }
            sourcePath = UserImageHistoryHelper.GetRealPathFor(session.User, sourcePath, root: root);
            if (!File.Exists(sourcePath))
            {
                failed++;
                continue;
            }
            string targetPath = $"{targetRoot}/{Path.GetFileName(sourcePath)}";
            if (File.Exists(targetPath))
            {
                string beforeDot = targetPath.BeforeLast('.');
                string ext = targetPath.AfterLast('.');
                int suffix = 1;
                while (File.Exists($"{beforeDot}-{suffix}.{ext}"))
                {
                    suffix++;
                }
                targetPath = $"{beforeDot}-{suffix}.{ext}";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            if (copyMode)
            {
                File.Copy(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
                OutputMetadataTracker.RemoveMetadataFor(sourcePath);
                RemoveHistoryIndexForPath(root, sourcePath);
            }
            string sourceBase = sourcePath.BeforeLast('.');
            string targetBase = targetPath.BeforeLast('.');
            foreach (string ext in DeletableFileExtensions)
            {
                string sourceAlt = $"{sourceBase}{ext}";
                if (!File.Exists(sourceAlt))
                {
                    continue;
                }
                if (copyMode)
                {
                    File.Copy(sourceAlt, $"{targetBase}{ext}", true);
                }
                else
                {
                    File.Move(sourceAlt, $"{targetBase}{ext}", true);
                }
            }
            RefreshHistoryIndexForPath(root, targetPath, session.User.Settings.StarNoFolders);
            changed++;
        }
        return new JObject() { ["success"] = true, ["changed"] = changed, ["failed"] = failed };
    }

    [API.APIDescription("Export the current Swarm image to a temporary PNG and open it in the local Krita application.", "\"success\": true")]
    public static async Task<JObject> SendImageToKrita(Session session,
        [API.APIParameter("The PNG-or-dataURL image content to export to Krita.")] string imageData)
    {
        return await KritaAPI.SendImageToKrita(session, imageData);
    }

    [API.APIDescription("Accept a flattened Krita image and store it as a pending import for the target Swarm session.", "\"success\": true")]
    public static async Task<JObject> ImportKritaImage(Session session,
        [API.APIParameter("Base64-encoded PNG bytes from the Krita bridge plugin.")] string imageBase64,
        [API.APIParameter("The session ID that should receive the returned Krita image.")] string targetSession)
    {
        return await KritaAPI.ImportKritaImage(session, imageBase64, targetSession);
    }

    [API.APIDescription("Check whether the current Swarm session has a pending Krita image import waiting to be applied.", "\"success\": true, \"image\": \"data:image/png;base64,...\"")]
    public static async Task<JObject> CheckPendingKritaImage(Session session)
    {
        return await KritaAPI.CheckPendingKritaImage(session);
    }

    [API.APIDescription("Get the current active local Swarm session targeted for Krita round-trips.", "\"success\": true, \"session_id\": \"...\"")]
    public static async Task<JObject> GetActiveKritaSession(HttpContext context)
    {
        return await KritaAPI.GetActiveKritaSession(context);
    }

    [API.APIDescription("Toggle whether an image is starred or not.", "\"new_state\": true")]
    public static async Task<JObject> ToggleImageStarred(Session session,
        [API.APIParameter("The path to the image to star.")] string path)
    {
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        path = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string pathBeforeDot = path.BeforeLast('.');
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string starBeforeDot = starPath.BeforeLast('.');
        if (!File.Exists(path))
        {
            if (wasStar && File.Exists(starPath))
            {
                Logs.Debug($"User {session.User.UserID} un-starred '{path}' without a raw, moving back to raw");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Move(starPath, path);
                foreach (string ext in DeletableFileExtensions)
                {
                    if (File.Exists($"{starBeforeDot}{ext}"))
                    {
                        File.Move($"{starBeforeDot}{ext}", $"{pathBeforeDot}{ext}");
                    }
                }
                OutputMetadataTracker.RemoveMetadataFor(path);
                OutputMetadataTracker.RemoveMetadataFor(starPath);
                RemoveHistoryIndexForPath(root, starPath);
                RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
                return new JObject() { ["new_state"] = false };
            }
            Logs.Warning($"User {session.User.UserID} tried to star image path '{origPath}' which maps to '{path}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot star." };
        }
        if (File.Exists(starPath))
        {
            Logs.Debug($"User {session.User.UserID} un-starred '{path}'");
            File.Delete(starPath);
            foreach (string ext in DeletableFileExtensions)
            {
                if (File.Exists($"{starBeforeDot}{ext}"))
                {
                    File.Delete($"{starBeforeDot}{ext}");
                }
            }
            OutputMetadataTracker.RemoveMetadataFor(path);
            OutputMetadataTracker.RemoveMetadataFor(starPath);
            RemoveHistoryIndexForPath(root, starPath);
            RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
            return new JObject() { ["new_state"] = false };
        }
        else
        {
            Logs.Debug($"User {session.User.UserID} starred '{path}'");
            Directory.CreateDirectory(Path.GetDirectoryName(starPath));
            File.Copy(path, starPath);
            foreach (string ext in DeletableFileExtensions)
            {
                if (File.Exists($"{pathBeforeDot}{ext}"))
                {
                    File.Copy($"{pathBeforeDot}{ext}", $"{starBeforeDot}{ext}");
                }
            }
            OutputMetadataTracker.RemoveMetadataFor(path);
            OutputMetadataTracker.RemoveMetadataFor(starPath);
            RefreshHistoryIndexForPath(root, path, session.User.Settings.StarNoFolders);
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
            return new JObject() { ["new_state"] = true };
        }
    }

    [API.APIDescription("Toggle whether an image is hidden in history or not.", "\"new_state\": true")]
    public static async Task<JObject> ToggleImageHidden(Session session,
        [API.APIParameter("The path to the image to hide or unhide.")] string path)
    {
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to hide image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot hide." };
        }
        string rawMarker = $"{rawPath.BeforeLast('.')}{OutputMetadataTracker.HiddenMarkerExtension}";
        string starMarker = $"{starPath.BeforeLast('.')}{OutputMetadataTracker.HiddenMarkerExtension}";
        bool currentlyHidden = (rawExists && File.Exists(rawMarker)) || (starExists && File.Exists(starMarker));
        bool newState = !currentlyHidden;
        static void setMarker(string marker, bool fileExists, bool hidden)
        {
            if (!fileExists)
            {
                return;
            }
            if (hidden)
            {
                File.WriteAllText(marker, "1");
            }
            else if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
        setMarker(rawMarker, rawExists, newState);
        if (rawPath != starPath)
        {
            setMarker(starMarker, starExists, newState);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        Logs.Debug($"User {session.User.UserID} {(newState ? "hid" : "unhid")} image '{origPath}'");
        return new JObject() { ["new_state"] = newState };
    }

    [API.APIDescription("Set a user rating for an image in history.", "\"rating\": 5")]
    public static async Task<JObject> SetImageRating(Session session,
        [API.APIParameter("The path to the image to rate.")] string path,
        [API.APIParameter("Rating value from 0 through 5.")] int rating)
    {
        if (rating < 0 || rating > 5)
        {
            return new JObject() { ["error"] = "Rating must be from 0 through 5." };
        }
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to rate image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot rate." };
        }
        static void setRating(string imagePath, bool fileExists, int newRating)
        {
            if (!fileExists)
            {
                return;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            metadata["rating"] = newRating;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        setRating(rawPath, rawExists, rating);
        if (rawPath != starPath)
        {
            setRating(starPath, starExists, rating);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["rating"] = rating };
    }

    [API.APIDescription("Add or remove user tags for an image in history.", "\"tags\": [\"tag\"]")]
    public static async Task<JObject> SetImageTags(Session session,
        [API.APIParameter("The path to the image to tag.")] string path,
        [API.APIParameter("Comma-separated tags to add or remove.")] string tags,
        [API.APIParameter("Tag mode, either add or remove.")] string mode)
    {
        List<string> tagList = [.. (tags ?? "").Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (tagList.Count == 0)
        {
            return new JObject() { ["error"] = "No tags were provided." };
        }
        bool shouldAdd = mode == "add";
        bool shouldRemove = mode == "remove";
        if (!shouldAdd && !shouldRemove)
        {
            return new JObject() { ["error"] = "Tag mode must be add or remove." };
        }
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to tag image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot tag." };
        }
        static JArray getTags(JObject metadata)
        {
            if (metadata["tags"] is JArray arr)
            {
                return arr;
            }
            if (metadata["tags"] is JValue value && value.Value is string text)
            {
                return new JArray(text.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)));
            }
            return new JArray();
        }
        static JArray setTags(string imagePath, bool fileExists, List<string> changedTags, bool addMode)
        {
            if (!fileExists)
            {
                return null;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            List<string> existing = [.. getTags(metadata).Select(t => $"{t}").Where(t => !string.IsNullOrWhiteSpace(t))];
            if (addMode)
            {
                foreach (string tag in changedTags)
                {
                    if (!existing.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Add(tag);
                    }
                }
            }
            else
            {
                existing = [.. existing.Where(t => !changedTags.Any(r => r.Equals(t, StringComparison.OrdinalIgnoreCase)))];
            }
            JArray result = new(existing);
            metadata["tags"] = result;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
            return result;
        }
        JArray currentTags = setTags(rawPath, rawExists, tagList, shouldAdd);
        if (rawPath != starPath)
        {
            JArray starTags = setTags(starPath, starExists, tagList, shouldAdd);
            if (currentTags is null)
            {
                currentTags = starTags;
            }
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["tags"] = currentTags ?? new JArray() };
    }

    [API.APIDescription("Set user notes for an image in history.", "\"notes\": \"text\"")]
    public static async Task<JObject> SetImageNotes(Session session,
        [API.APIParameter("The path to the image to annotate.")] string path,
        [API.APIParameter("Note text to save.")] string notes)
    {
        notes ??= "";
        bool wasStar = false;
        path = path.Replace('\\', '/').Trim('/');
        if (path.StartsWith("Starred/"))
        {
            wasStar = true;
            path = path["Starred/".Length..];
        }
        string origPath = path;
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, session.User.OutputDirectory);
        (path, string consoleError, string userError) = WebServer.CheckFilePath(root, path);
        if (consoleError is not null)
        {
            Logs.Error(consoleError);
            return new JObject() { ["error"] = userError };
        }
        string rawPath = UserImageHistoryHelper.GetRealPathFor(session.User, path, root: root);
        string starPath = $"Starred/{(session.User.Settings.StarNoFolders ? origPath.Replace("/", "") : origPath)}";
        (starPath, _, _) = WebServer.CheckFilePath(root, starPath);
        starPath = UserImageHistoryHelper.GetRealPathFor(session.User, starPath, root: root);
        string primaryPath = wasStar ? starPath : rawPath;
        bool rawExists = File.Exists(rawPath);
        bool starExists = File.Exists(starPath);
        if (!File.Exists(primaryPath) && !rawExists && !starExists)
        {
            Logs.Warning($"User {session.User.UserID} tried to annotate image path '{origPath}' which maps to '{primaryPath}', but cannot as the image does not exist.");
            return new JObject() { ["error"] = "That file does not exist, cannot annotate." };
        }
        static void setNotes(string imagePath, bool fileExists, string newNotes)
        {
            if (!fileExists)
            {
                return;
            }
            string sidecarPath = $"{imagePath.BeforeLast('.')}.swarm.json";
            JObject metadata = new();
            if (File.Exists(sidecarPath))
            {
                try
                {
                    metadata = File.ReadAllText(sidecarPath).ParseToJson();
                }
                catch (Exception)
                {
                    metadata = new();
                }
            }
            metadata["notes"] = newNotes;
            File.WriteAllText(sidecarPath, metadata.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        setNotes(rawPath, rawExists, notes);
        if (rawPath != starPath)
        {
            setNotes(starPath, starExists, notes);
        }
        if (rawExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(rawPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, rawPath, session.User.Settings.StarNoFolders);
        }
        if (starExists)
        {
            OutputMetadataTracker.RemoveMetadataFor(starPath.Replace('\\', '/'));
            RefreshHistoryIndexForPath(root, starPath, session.User.Settings.StarNoFolders);
        }
        return new JObject() { ["notes"] = notes };
    }

    public static SemaphoreSlim RefreshSemaphore = new(1, 1);

    public static long LastRefreshed = Environment.TickCount64;

    [API.APIDescription("Trigger a refresh of the server's data, returning parameter data. Requires permission 'control_model_refresh' to actually take effect, otherwise just pulls latest data.",
        """
            // see `ListT2IParams` for details
            "list": [...],
            "groups": [...],
            "models": [...],
            "wildcards": [...],
            "param_edits": [...]
        """)]
    public static async Task<JObject> TriggerRefresh(Session session,
        [API.APIParameter("If true, fully refresh everything. If false, just grabs the list of current available parameters (waiting for any pending refreshes first).")] bool strong = true,
        [API.APIParameter("Optional type of data to refresh. If unspecified, runs a general refresh. Valid options: ['wildcards']")] string refreshType = null)
    {
        Logs.Verbose($"User {session.User.UserID} triggered a {(strong ? "strong" : "weak")} data refresh");
        bool botherToRun = strong && RefreshSemaphore.CurrentCount > 0; // no need to run twice at once
        if (botherToRun && Environment.TickCount64 - LastRefreshed < 10000)
        {
            Logs.Debug($"User {session.User.UserID} requested weak refresh within 10 seconds of last refresh, ignoring as redundant.");
            botherToRun = false;
        }
        if (!session.User.HasPermission(Permissions.ControlModelRefresh))
        {
            Logs.Debug($"User {session.User.UserID} requested refresh, but will not perform actual refresh as they lack permission.");
            botherToRun = false;
        }
        try
        {
            await RefreshSemaphore.WaitAsync(Program.GlobalProgramCancel);
            if (botherToRun)
            {
                using ManyReadOneWriteLock.WriteClaim claim = Program.RefreshLock.LockWrite();
                if (string.IsNullOrWhiteSpace(refreshType))
                {
                    Program.ModelRefreshEvent?.Invoke();
                    LastRefreshed = Environment.TickCount64;
                }
                else if (refreshType == "wildcards")
                {
                    WildcardsHelper.Refresh();
                }
                else
                {
                    Logs.Warning($"User {session.User.UserID} requested refresh type '{refreshType}' which is unrecognized, ignoring.");
                }
            }
        }
        finally
        {
            RefreshSemaphore.Release();
        }
        Logs.Debug($"Data refreshed!");
        return await ListT2IParams(session);
    }

    [API.APIDescription("Get a list of available T2I parameters.",
        """
        "list":
        [
            {
                "name": "Param Name Here",
                "id": "paramidhere",
                "description": "parameter description here",
                "type": "type", // text, integer, etc
                "subtype": "Stable-Diffusion", // can be null
                "default": "default value here",
                "min": 0,
                "max": 10,
                "view_max": 10,
                "step": 1,
                "values": ["value1", "value2"], // or null
                "examples": ["example1", "example2"], // or null
                "visible": true,
                "advanced": false,
                "feature_flag": "flagname", // or null
                "toggleable": true,
                "priority": 0,
                "group": "idhere", // or null
                "always_retain": false,
                "do_not_save": false,
                "do_not_preview": false,
                "view_type": "big", // dependent on type
                "extra_hidden": false
            }
        ],
        "groups":
        [
            {
                "name": "Group Name Here",
                "id": "groupidhere",
                "toggles": true,
                "open": false,
                "priority": 0,
                "description": "group description here",
                "advanced": false,
                "can_shrink": true,
                "parent": "idhere" // or null
            }
        ],
        "model_compat_classes":
        {
            "stable-diffusion-xl-v1": {"shortcode": "SDXL", ... },
            // etc
        },
        "model_classes":
        {
            "stable-diffusion-xl-v1-base": {"compat_class": "stable-diffusion-xl-v1", ... },
            // etc
        }
        "models":
        {
            "Stable-Diffusion": [["model1", "archid"], ["model2", "archid"]],
            "LoRA": [["model1", "archid"], ["model2", "archid"]],
            // etc
        },
        "wildcards": ["wildcard1", "wildcard2"],
        "param_edits": // can be null
        {
            // (This is interface-specific data)
        }
        """)]
    public static async Task<JObject> ListT2IParams(Session session)
    {
        JObject modelData = [];
        foreach (T2IModelHandler handler in Program.T2IModelSets.Values)
        {
            modelData[handler.ModelType] = new JArray(handler.ListModelsFor(session).OrderBy(m => m.Name).Select(m => new JArray(m.Name, m.ModelClass?.ID)).ToArray());
        }
        T2IParamType[] types = [.. T2IParamTypes.Types.Values.Where(p => p.Permission is null || session.User.HasPermission(p.Permission))];
        Dictionary<string, T2IParamGroup> groups = new(64);
        foreach (T2IParamType type in types)
        {
            T2IParamGroup group = type.Group;
            while (group is not null)
            {
                groups[group.ID] = group;
                group = group.Parent;
            }
        }
        JObject modelCompatClasses = [];
        foreach (T2IModelCompatClass clazz in T2IModelClassSorter.CompatClasses.Values)
        {
            modelCompatClasses[clazz.ID] = clazz.ToNetData();
        }
        JObject modelClasses = [];
        foreach (T2IModelClass clazz in T2IModelClassSorter.ModelClasses.Values)
        {
            modelClasses[clazz.ID] = clazz.ToNetData();
        }
        return new JObject()
        {
            ["list"] = new JArray(types.Select(v => v.ToNet(session)).ToList()),
            ["groups"] = new JArray(groups.Values.OrderBy(g => g.OrderPriority).Select(g => g.ToNet(session)).ToList()),
            ["models"] = modelData,
            ["model_compat_classes"] = modelCompatClasses,
            ["model_classes"] = modelClasses,
            ["wildcards"] = new JArray(WildcardsHelper.ListFiles),
            ["param_edits"] = string.IsNullOrWhiteSpace(session.User.Data.RawParamEdits) ? null : JObject.Parse(session.User.Data.RawParamEdits)
        };
    }
}
