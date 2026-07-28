using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Diagnostics;
using System.IO;

namespace SwarmUI.WebAPI;

[API.APIClass("General utility API routes.")]
public static class UtilAPI
{
    /// <summary>Shared identity for one core lazy-loaded generation-page tab.</summary>
    public record class LazyGenPageTabDescriptor
    {
        /// <summary>Client-safe key used to identify the lazy tab.</summary>
        public string Key { get; }

        /// <summary>DOM ID of the lazy tab content element.</summary>
        public string TabId { get; }

        /// <summary>DOM ID of the lazy tab navigation button.</summary>
        public string ButtonId { get; }

        /// <summary>User-facing label shown for the lazy tab.</summary>
        public string DisplayLabel { get; }

        /// <summary>Razor partial name rendered for the lazy tab.</summary>
        public string Partial { get; }

        /// <summary>Loading text shown while the lazy tab partial is fetched.</summary>
        public string LoadingText { get; }

        /// <summary>Permission required to request the lazy tab partial.</summary>
        public PermInfo Permission { get; }

        /// <summary>Whether the navigation header declares the tab permission requirement.</summary>
        public bool ShowPermissionOnHeader { get; }

        /// <summary>Constructs a shared core lazy-tab descriptor.</summary>
        /// <param name="key">Client-safe key used to identify the lazy tab.</param>
        /// <param name="tabId">DOM ID of the lazy tab content element.</param>
        /// <param name="buttonId">DOM ID of the lazy tab navigation button.</param>
        /// <param name="displayLabel">User-facing label shown for the lazy tab.</param>
        /// <param name="partial">Razor partial name rendered for the lazy tab.</param>
        /// <param name="loadingText">Loading text shown while the lazy tab partial is fetched.</param>
        /// <param name="permission">Permission required to request the lazy tab partial.</param>
        /// <param name="showPermissionOnHeader">Whether the navigation header declares the tab permission requirement.</param>
        public LazyGenPageTabDescriptor(string key, string tabId, string buttonId, string displayLabel,
            string partial, string loadingText, PermInfo permission, bool showPermissionOnHeader)
        {
            Key = key;
            TabId = tabId;
            ButtonId = buttonId;
            DisplayLabel = displayLabel;
            Partial = partial;
            LoadingText = loadingText;
            Permission = permission;
            ShowPermissionOnHeader = showPermissionOnHeader;
        }
    }

    /// <summary>Ordered shared descriptors for the core lazy-loaded generation-page tabs.</summary>
    public static readonly IReadOnlyList<LazyGenPageTabDescriptor> LazyGenPageTabs = new List<LazyGenPageTabDescriptor>()
    {
        new("imageediting", "ImageEditing", "imageeditingtabbutton", "Image Editing", "_Generate/ImageEditingTab",
            "Loading image editor...", Permissions.FundamentalGenerateTabAccess, false),
        new("utilities", "utilities_tab", "utilitiestabbutton", "Utilities", "_Generate/UtilitiesTab",
            "Loading utilities...", Permissions.UtilitiesTab, true),
        new("user", "user_tab", "usersettingstabbutton", "User", "_Generate/UserTab",
            "Loading user settings...", Permissions.UserTab, true),
        new("server", "server_tab", "servertabbutton", "Server", "_Generate/ServerTab",
            "Loading server tools...", Permissions.ViewServerTab, true)
    }.AsReadOnly();

    /// <summary>Builds the compatible server-rendered partial allowlist from the shared lazy-tab descriptors.</summary>
    private static IReadOnlyDictionary<string, (string PartialView, PermInfo Permission)> BuildLazyGenPageTabPartials()
    {
        Dictionary<string, (string PartialView, PermInfo Permission)> partials = new();
        foreach (LazyGenPageTabDescriptor tab in LazyGenPageTabs)
        {
            partials.Add(tab.Key, ($"/Pages/{tab.Partial}.cshtml", tab.Permission));
        }
        return partials;
    }

    /// <summary>Allowlisted lazy genpage tab partials keyed by client-safe tab identifiers.</summary>
    public static readonly IReadOnlyDictionary<string, (string PartialView, PermInfo Permission)> LazyGenPageTabPartials
        = BuildLazyGenPageTabPartials();

    public static void Register()
    {
        API.RegisterAPICall(GetGenPageTabPartial, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(CountTokens, false, Permissions.UseTokenizer);
        API.RegisterAPICall(TokenizeInDetail, false, Permissions.UseTokenizer);
        API.RegisterAPICall(Pickle2SafeTensor, true, Permissions.Pickle2Safetensors);
        API.RegisterAPICall(WipeMetadata, true, Permissions.ResetMetadata);
    }

    public static ConcurrentDictionary<string, CliplikeTokenizer> Tokenizers = new();

    [API.APIDescription("Fetches rendered HTML for an allowlisted lazy-loaded generation page tab partial.", """
        "html": "<div>...</div>"
    """)]
    public static async Task<JObject> GetGenPageTabPartial(HttpContext context, Session session,
        [API.APIParameter("The allowlisted lazy tab key to render.")] string tab)
    {
        tab = (tab ?? "").Trim().ToLowerFast();
        if (!LazyGenPageTabPartials.TryGetValue(tab, out (string PartialView, PermInfo Permission) partialInfo))
        {
            return Utilities.ErrorObj("Invalid tab key.", "invalid_tab");
        }
        if (partialInfo.Permission is not null && !session.User.HasPermission(partialInfo.Permission))
        {
            return Utilities.ErrorObj("You lack permissions for this tab.", "bad_permissions");
        }
        string html = await WebServer.RenderPartialViewToString(context, partialInfo.PartialView, new GeneratePageModel(context));
        return new JObject() { ["html"] = html };
    }

    private static (JObject, CliplikeTokenizer) GetTokenizerForAPI(string text, string tokenset)
    {
        if (text.Length > 100 * 1024)
        {
            return (new JObject() { ["error"] = "Text too long, refused." }, null);
        }
        tokenset = Utilities.FilePathForbidden.TrimToNonMatches(tokenset);
        if (tokenset.Contains('/') || tokenset.Contains('.') || tokenset.Trim() == "" || tokenset.Length > 128)
        {
            return (new JObject() { ["error"] = "Invalid tokenset (refused characters or format), refused." }, null);
        }
        try
        {
            CliplikeTokenizer tokenizer = Tokenizers.GetOrCreate(tokenset, () =>
            {
                string fullPath = $"src/srcdata/Tokensets/{tokenset}.txt.gz";
                if (!File.Exists(fullPath))
                {
                    throw new SwarmUserErrorException($"Tokenset '{tokenset}' does not exist.");
                }
                CliplikeTokenizer tokenizer = new();
                tokenizer.Load(fullPath);
                return tokenizer;
            });
            return (null, tokenizer);
        }
        catch (SwarmReadableErrorException ex)
        {
            return (new JObject() { ["error"] = ex.Message }, null);
        }
    }

    private static readonly string[] SkippablePromptSyntax = ["segment", "object", "region", "clear", "extend"];

    /// <summary>Builds display ranges for prompt split indicators. Ranges are based on the raw prompt text so the browser can map them back onto the textarea content.</summary>
    private static JObject BuildPromptSplitMetadata(string text, CliplikeTokenizer tokenizer)
    {
        JArray segments = [];
        JArray breaks = [];
        int segmentIndex = 0;

        void addSegment(int start, int end, int tokens, string reason)
        {
            if (end <= start)
            {
                return;
            }
            segments.Add(new JObject()
            {
                ["start"] = start,
                ["end"] = end,
                ["tokens"] = tokens,
                ["index"] = segmentIndex++,
                ["reason"] = reason
            });
        }

        void addBreak(int start, int end, string type)
        {
            breaks.Add(new JObject()
            {
                ["start"] = start,
                ["end"] = end,
                ["type"] = type
            });
        }

        void addSection(int start, int end, string endReason)
        {
            int segmentStart = start;
            int tokenCount = 0;
            foreach (System.Text.RegularExpressions.Match match in CliplikeTokenizer.Splitter.Matches(text[start..end]))
            {
                if (string.IsNullOrWhiteSpace(match.Value))
                {
                    continue;
                }
                int wordStart = start + match.Index;
                int wordEnd = wordStart + match.Length;
                int wordTokens = tokenizer.Encode(match.Value).Length;
                if (tokenCount > 0 && tokenCount + wordTokens > 75)
                {
                    addSegment(segmentStart, wordStart, tokenCount, "token_limit");
                    segmentStart = wordStart;
                    tokenCount = 0;
                }
                tokenCount += wordTokens;
                if (tokenCount >= 75)
                {
                    addSegment(segmentStart, wordEnd, tokenCount, "token_limit");
                    segmentStart = wordEnd;
                    tokenCount = 0;
                }
            }
            addSegment(segmentStart, end, tokenCount, endReason);
        }

        int sectionStart = 0;
        while (sectionStart <= text.Length)
        {
            int breakAt = text.IndexOf("<break>", sectionStart, StringComparison.Ordinal);
            int sectionEnd = breakAt == -1 ? text.Length : breakAt;
            addSection(sectionStart, sectionEnd, breakAt == -1 ? "end" : "forced");
            if (breakAt == -1)
            {
                break;
            }
            int splitEnd = breakAt + "<break>".Length;
            addBreak(breakAt, splitEnd, "forced");
            sectionStart = splitEnd;
        }

        return new JObject()
        {
            ["segments"] = segments,
            ["breaks"] = breaks
        };
    }

    [API.APIDescription("Count the CLIP-like tokens in a given text prompt.", "\"count\": 0")]
    public static async Task<JObject> CountTokens(
        [API.APIParameter("The text to tokenize.")] string text,
        [API.APIParameter("If false, process prompt syntax (things like `<random:`) as if its regular text. If true, clean it up first.")] bool skipPromptSyntax = false,
        [API.APIParameter("What tokenization set to use.")] string tokenset = "clip",
        [API.APIParameter("If true, process weighting (like `(word:1.5)`). If false, don't process that.")] bool weighting = true)
    {
        string rawText = text;
        if (skipPromptSyntax)
        {
            foreach (string str in SkippablePromptSyntax)
            {
                int skippable = text.IndexOf($"<{str}:");
                if (skippable != -1)
                {
                    text = text[..skippable];
                }
            }
            text = T2IPromptHandling.ProcessPromptLikeForLength(text);
        }
        (JObject error, CliplikeTokenizer tokenizer) = GetTokenizerForAPI(text, tokenset);
        if (error is not null)
        {
            return error;
        }
        if (!weighting)
        {
            CliplikeTokenizer.Token[] rawTokens = tokenizer.Encode(text);
            return new JObject() { ["count"] = rawTokens.Length };
        }
        string[] sections = text.Split("<break>");
        int biggest = sections.Select(text => tokenizer.EncodeWithWeighting(text).Length).Max();
        return new JObject() { ["count"] = biggest, ["split_data"] = BuildPromptSplitMetadata(rawText, tokenizer) };
    }

    [API.APIDescription("Tokenize some prompt text and get thorough detail about it.",
        """
            "tokens":
            [
                {
                    "id": 123,
                    "weight": 1.0,
                    "text": "tok"
                }
            ]
        """)]
    public static async Task<JObject> TokenizeInDetail(
        [API.APIParameter("The text to tokenize.")] string text,
        [API.APIParameter("If false, process prompt syntax (things like `<random:`) as if its regular text. If true, clean it up first.")] bool skipPromptSyntax = false,
        [API.APIParameter("What tokenization set to use.")] string tokenset = "clip",
        [API.APIParameter("If true, process weighting (like `(word:1.5)`). If false, don't process that.")] bool weighting = true)
    {
        if (skipPromptSyntax)
        {
            foreach (string str in SkippablePromptSyntax)
            {
                int skippable = text.IndexOf($"<{str}:");
                if (skippable != -1)
                {
                    text = text[..skippable];
                }
            }
            text = T2IPromptHandling.ProcessPromptLikeForLength(text);
        }
        (JObject error, CliplikeTokenizer tokenizer) = GetTokenizerForAPI(text, tokenset);
        if (error is not null)
        {
            return error;
        }
        CliplikeTokenizer.Token[] tokens = weighting ? tokenizer.EncodeWithWeighting(text) : tokenizer.Encode(text);
        return new JObject()
        {
            ["tokens"] = new JArray(tokens.Select(t => new JObject() { ["id"] = t.ID, ["weight"] = t.Weight, ["text"] = tokenizer.Tokens[t.ID] }).ToArray())
        };
    }

    [API.APIDescription("Trigger bulk conversion of models from pickle format to safetensors.", "\"success\": true")]
    public static async Task<JObject> Pickle2SafeTensor(
        [API.APIParameter("What type of model to convert, eg `Stable-Diffusion`, `LoRA`, etc.")] string type,
        [API.APIParameter("If true, convert to fp16 while processing. If false, use original model's weight type.")] bool fp16)
    {
        string[] folderPaths;
        using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
        {
            if (!Program.T2IModelSets.TryGetValue(type, out T2IModelHandler models))
            {
                return new JObject() { ["error"] = $"Invalid type '{type}'." };
            }
            folderPaths = [.. models.FolderPaths];
        }
        foreach (string path in folderPaths)
        {
            Process p = PythonLaunchHelper.LaunchGeneric("launchtools/pickle-to-safetensors.py", true, [path, fp16 ? "true" : "false"]);
            await p.WaitForExitAsync(Program.GlobalProgramCancel);
        }
        return new JObject() { ["success"] = true };
    }

    [API.APIDescription("Trigger a mass metadata reset.", "\"success\": true")]
    public static async Task<JObject> WipeMetadata()
    {
        BackendHandler.BackendData[] backends = [.. Program.Backends.AllBackends.Values];
        foreach (BackendHandler.BackendData backend in backends)
        {
            Interlocked.Add(ref backend.Usages, backend.AbstractBackend.MaxUsages);
        }
        try
        {
            int ticks = 0;
            while (Program.Backends.AllBackends.Values.Any(b => b.Usages > b.AbstractBackend.MaxUsages))
            {
                if (Program.GlobalProgramCancel.IsCancellationRequested)
                {
                    return null;
                }
                await Task.Delay(TimeSpan.FromSeconds(0.5));
                if (ticks > 240)
                {
                    Logs.Info($"Reset All Metadata: stuck waiting for backends to be clear too long, will just do it anyway.");
                    break;
                }
            }
            using (ManyReadOneWriteLock.ReadClaim claim = Program.RefreshLock.LockRead())
            {
                foreach (T2IModelHandler handler in Program.T2IModelSets.Values)
                {
                    handler.MassRemoveMetadata();
                }
            }
        }
        finally
        {
            foreach (BackendHandler.BackendData backend in backends)
            {
                Interlocked.Add(ref backend.Usages, -backend.AbstractBackend.MaxUsages);
            }
        }
        OutputMetadataTracker.MassRemoveMetadata();
        T2IAPI.LastRefreshed = Environment.TickCount64 - 20000;
        return new JObject() { ["success"] = true };
    }
}
