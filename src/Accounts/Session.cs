using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using LiteDB;
using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Collections.Generic;
using System.IO;

namespace SwarmUI.Accounts;

/// <summary>Container for information related to an active session.</summary>
public class Session : IEquatable<Session>
{
    /// <summary>Database entry for <see cref="Session"/> persistence data.</summary>
    public class DatabaseEntry
    {
        /// <summary>The randomly generated session ID.</summary>
        [BsonId]
        public string ID { get; set; }

        /// <summary>The relevant user's ID.</summary>
        public string UserID { get; set; }

        /// <summary>Timestamp (Unix seconds) of when this session was last actively used.</summary>
        public long LastActiveUnixTime { get; set; }

        /// <summary>Originating remote IP address of the session.</summary>
        public string OriginAddress { get; set; }

        /// <summary>If authorization is enabled, this is the token ID that created this session.</summary>
        public string OriginToken { get; set; }
    }

    /// <summary>Randomly generated ID.</summary>
    public string ID;

    /// <summary>The relevant <see cref="User"/>.</summary>
    public User User;

    /// <summary>If authorization is enabled, this is the token ID that created this session.</summary>
    public string OriginToken;

    /// <summary>If true, this session persists across restarts. If false, it sits only in memory.</summary>
    public bool Persist = true;

    /// <summary>The current database entry for this <see cref="Session"/>.</summary>
    public DatabaseEntry MakeDBEntry()
    {
        return new DatabaseEntry()
        {
            ID = ID,
            UserID = User.UserID,
            LastActiveUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)TimeSinceLastUsed.TotalSeconds,
            OriginAddress = OriginAddress,
            OriginToken = OriginToken
        };
    }

    /// <summary>Token to interrupt this session.</summary>
    public CancellationTokenSource SessInterrupt = new();

    /// <summary>All current generation claims.</summary>
    public ConcurrentDictionary<long, GenClaim> Claims = [];

    /// <summary>Statistics about the generations currently waiting in this session.</summary>
    public int WaitingGenerations = 0, LoadingModels = 0, WaitingBackends = 0, LiveGens = 0;

    /// <summary><see cref="Environment.TickCount64"/> value for the last time this session triggered a generation, updated a setting, or other 'core action'.</summary>
    public long LastUsedTime = Environment.TickCount64;

    /// <summary>Originating remote IP address of the session.</summary>
    public string OriginAddress;

    /// <summary>Updates the <see cref="LastUsedTime"/> to the current time.</summary>
    public void UpdateLastUsedTime()
    {
        Volatile.Write(ref LastUsedTime, Environment.TickCount64);
        User.UpdateLastUsedTime();
    }

    /// <summary>Time since the last action was performed in this session.</summary>
    public TimeSpan TimeSinceLastUsed => TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref LastUsedTime));

    /// <summary>Use "using <see cref="GenClaim"/> claim = session.Claim(image_count);" to track generation requests pending on this session.</summary>
    public GenClaim Claim(int gens = 0, int modelLoads = 0, int backendWaits = 0, int liveGens = 0)
    {
        return new(this, gens, modelLoads, backendWaits, liveGens);
    }

    /// <summary>Helper to claim an amount of generations and dispose it automatically cleanly.</summary>
    public class GenClaim : IDisposable
    {
        /// <summary>Current number used to generate <see cref="ID"/>.</summary>
        public static long ClaimID = 0;

        /// <summary>The number of generations tracked by this object.</summary>
        public int WaitingGenerations = 0, LoadingModels = 0, WaitingBackends = 0, LiveGens = 0;

        /// <summary>The relevant original session.</summary>
        public Session Sess;

        /// <summary>Cancel token that cancels if the user wants to interrupt all generations.</summary>
        public CancellationToken InterruptToken;

        /// <summary>Token source to interrupt just this claim's set.</summary>
        public CancellationTokenSource LocalClaimInterrupt = new();

        /// <summary>Unique claim ID from an incremental number.</summary>
        public long ID = Interlocked.Increment(ref ClaimID);

        /// <summary>If true, the running generations should stop immediately.</summary>
        public bool ShouldCancel => InterruptToken.IsCancellationRequested || LocalClaimInterrupt.IsCancellationRequested;

        public GenClaim(Session session, int gens, int modelLoads, int backendWaits, int liveGens)
        {
            Sess = session;
            InterruptToken = session.SessInterrupt.Token;
            Extend(gens, modelLoads, backendWaits, liveGens);
            session.Claims[ID] = this;
        }

        /// <summary>Increase the size of the claim.</summary>
        public void Extend(int gens = 0, int modelLoads = 0, int backendWaits = 0, int liveGens = 0)
        {
            Interlocked.Add(ref WaitingGenerations, gens);
            Interlocked.Add(ref LoadingModels, modelLoads);
            Interlocked.Add(ref WaitingBackends, backendWaits);
            Interlocked.Add(ref LiveGens, liveGens);
            Interlocked.Add(ref Sess.WaitingGenerations, gens);
            Interlocked.Add(ref Sess.LoadingModels, modelLoads);
            Interlocked.Add(ref Sess.WaitingBackends, backendWaits);
            Interlocked.Add(ref Sess.LiveGens, liveGens);
        }

        /// <summary>Mark a subset of these as complete.</summary>
        public void Complete(int gens = 0, int modelLoads = 0, int backendWaits = 0, int liveGens = 0)
        {
            Extend(-gens, -modelLoads, -backendWaits, -liveGens);
        }

        /// <summary>Internal dispose route, called by 'using' statements.</summary>
        public void Dispose()
        {
            Complete(WaitingGenerations, LoadingModels, WaitingBackends, LiveGens);
            Sess.Claims.TryRemove(ID, out _);
            LocalClaimInterrupt.Dispose();
            GC.SuppressFinalize(this);
        }

        ~GenClaim()
        {
            Dispose();
        }
    }

    /// <summary>Applies metadata to an image and converts the filetype, following the user's preferences.</summary>
    public (Task<MediaFile>, string) ApplyMetadata(MediaFile file, T2IParamInput user_input, int numImagesGenned, bool maySkipConversion = false)
    {
        if (numImagesGenned > 0 && user_input.TryGet(T2IParamTypes.BatchSize, out int batchSize) && numImagesGenned < batchSize)
        {
            user_input = user_input.Clone();
            if (user_input.TryGet(T2IParamTypes.VariationSeed, out long varSeed) && user_input.Get(T2IParamTypes.VariationSeedStrength) > 0)
            {
                user_input.Set(T2IParamTypes.VariationSeed, varSeed + numImagesGenned);
            }
            else
            {
                user_input.Set(T2IParamTypes.Seed, user_input.Get(T2IParamTypes.Seed) + numImagesGenned);
            }
        }
        JObject metadataObj = user_input.GenFullMetadataObject();
        if (file is ImageFile metadataImage)
        {
            (int finalWidth, int finalHeight) = metadataImage.GetResolution();
            JObject extraData = metadataObj["sui_extra_data"] as JObject;
            if (extraData is null)
            {
                extraData = new JObject();
                metadataObj["sui_extra_data"] = extraData;
            }
            extraData["final_width"] = finalWidth;
            extraData["final_height"] = finalHeight;
        }
        string metadata = T2IParamInput.MetadataToString(metadataObj);
        Task<MediaFile> resultImg = Task.FromResult(file);
        if (file is ImageFile img && (!maySkipConversion || !user_input.Get(T2IParamTypes.DoNotSave, false) || user_input.SourceSession.User.Settings.FileFormat.ReformatTransientImages))
        {
            string format = user_input.Get(T2IParamTypes.ImageFormat, User.Settings.FileFormat.ImageFormat);
            resultImg = Task.Run<MediaFile>(() =>
            {
                try
                {
                    return img.ConvertTo(format, User.Settings.FileFormat.SaveMetadata ? metadata : null, User.Settings.FileFormat.DPI, Math.Clamp(User.Settings.FileFormat.ImageQuality, 1, 100), User.Settings.FileFormat.StealthMetadata);
                }
                catch (Exception ex)
                {
                    Logs.Error($"Internal error in async task: {ex.ReadableString()}");
                    return null;
                }
            });
        }
        // TODO: Metadata for audio, video, ...?
        return (resultImg, metadata ?? "");
    }

    /// <summary>Special cache of recently blocked image filenames (eg file deleted, or may be saving), to prevent generating new images with exact same filenames.</summary>
    public static ConcurrentDictionary<string, string> RecentlyBlockedFilenames = [];

    /// <summary>File data that will be saved soon, or has very recently saved.</summary>
    public static ConcurrentDictionary<string, Task<byte[]>> StillSavingFiles = [];

    /// <summary>Opaque ownership handle for one maintained output filename reservation.</summary>
    internal readonly record struct OutputFilenameReservation(string Path, long Generation);

    /// <summary>Serializes maintained output filename reservation publication, release, and clearing.</summary>
    private static readonly LockObject OutputFilenameReservationLock = new();

    /// <summary>Lifecycle state for one maintained output filename reservation generation.</summary>
    private readonly record struct OutputFilenameReservationState(bool IsActive);

    /// <summary>Maintained output filename reservation owners by normalized full path and generation.</summary>
    private static readonly Dictionary<string, Dictionary<long, OutputFilenameReservationState>> MaintainedOutputFilenameReservations = [];

    /// <summary>Monotonic generation source for maintained output filename reservations.</summary>
    private static long OutputFilenameReservationGeneration;

    /// <summary>How long a failed save or successful deletion keeps an expiring inactive filename reservation.</summary>
    private static readonly TimeSpan InactiveOutputFilenameReservationLifetime = TimeSpan.FromSeconds(10);

    /// <summary>Best-effort diagnostic for output transient-state cleanup failures.</summary>
    private static void LogOutputCleanupFailure(string action, Exception ex)
    {
        try
        {
            Logs.Error($"Internal error while {action}: {ex.ReadableString()}");
        }
        catch
        {
            // Cleanup diagnostics must not mask the save or deletion failure being handled.
        }
    }

    /// <summary>Attempts to reserve a persisted output path without colliding with any extensionless public reservation.</summary>
    private static bool TryReserveOutputFilename(string fullPath,
        out OutputFilenameReservation reservation,
        OutputFilenameSelectionAttempt measurement)
    {
        lock (OutputFilenameReservationLock)
        {
            string fullPathNoExt = fullPath.BeforeLast('.');
            bool hasCollision;
            if (measurement is null)
            {
                hasCollision = RecentlyBlockedFilenames.Keys.Any(
                    path => path.BeforeLast('.') == fullPathNoExt);
            }
            else
            {
                int examined = 0;
                long scanStart = OutputFilenameSelectionMeasurement.Timestamp();
                ICollection<string> reservationKeys = RecentlyBlockedFilenames.Keys;
                hasCollision = reservationKeys.Any(path =>
                {
                    examined++;
                    return path.BeforeLast('.') == fullPathNoExt;
                });
                measurement.ReservationScanMicroseconds +=
                    OutputFilenameSelectionMeasurement.ElapsedMicroseconds(scanStart);
                measurement.ReservationKeyCount = Math.Max(
                    measurement.ReservationKeyCount, reservationKeys.Count);
                measurement.ReservationKeysExamined += examined;
                if (hasCollision)
                {
                    measurement.ReservationCollisionMatches++;
                }
            }
            if (hasCollision)
            {
                reservation = default;
                return false;
            }
            long generation = Interlocked.Increment(ref OutputFilenameReservationGeneration);
            if (!MaintainedOutputFilenameReservations.TryGetValue(fullPath, out Dictionary<long, OutputFilenameReservationState> owners))
            {
                owners = [];
                MaintainedOutputFilenameReservations[fullPath] = owners;
            }
            owners.Add(generation, new OutputFilenameReservationState(true));
            try
            {
                RecentlyBlockedFilenames[fullPath] = fullPath;
            }
            catch
            {
                owners.Remove(generation);
                if (owners.Count == 0)
                {
                    MaintainedOutputFilenameReservations.Remove(fullPath);
                }
                throw;
            }
            reservation = new OutputFilenameReservation(fullPath, generation);
            return true;
        }
    }

    /// <summary>Attempts to add a deletion reservation owner when the exact path has no active maintained owner.</summary>
    internal static bool TryReserveDeletedOutputFilename(string fullPath, out OutputFilenameReservation handle)
    {
        lock (OutputFilenameReservationLock)
        {
            if (MaintainedOutputFilenameReservations.TryGetValue(fullPath, out Dictionary<long, OutputFilenameReservationState> owners))
            {
                foreach (OutputFilenameReservationState owner in owners.Values)
                {
                    if (owner.IsActive)
                    {
                        handle = default;
                        return false;
                    }
                }
            }
            else
            {
                owners = [];
                MaintainedOutputFilenameReservations[fullPath] = owners;
            }
            long generation = Interlocked.Increment(ref OutputFilenameReservationGeneration);
            owners.Add(generation, new OutputFilenameReservationState(true));
            try
            {
                RecentlyBlockedFilenames[fullPath] = fullPath;
            }
            catch
            {
                owners.Remove(generation);
                if (owners.Count == 0)
                {
                    MaintainedOutputFilenameReservations.Remove(fullPath);
                }
                throw;
            }
            handle = new OutputFilenameReservation(fullPath, generation);
            return true;
        }
    }

    /// <summary>Best-effort removal of the maintained filename reservation generation identified by a handle.</summary>
    private static void RemoveOutputFilenameReservation(OutputFilenameReservation reservation)
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                if (!MaintainedOutputFilenameReservations.TryGetValue(reservation.Path, out Dictionary<long, OutputFilenameReservationState> owners)
                    || !owners.Remove(reservation.Generation))
                {
                    return;
                }
                if (owners.Count == 0)
                {
                    MaintainedOutputFilenameReservations.Remove(reservation.Path);
                    RecentlyBlockedFilenames.TryRemove(reservation.Path, out _);
                }
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"removing output filename reservation '{reservation.Path}'", ex);
        }
    }

    /// <summary>Immediately releases a matching successful-save filename reservation.</summary>
    private static void ReleaseOutputFilenameReservation(OutputFilenameReservation reservation)
    {
        RemoveOutputFilenameReservation(reservation);
    }

    /// <summary>Transitions a matching active reservation to inactive before its terminal policy is selected.</summary>
    private static bool TryTransitionOutputFilenameReservationToInactive(OutputFilenameReservation reservation)
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                if (!MaintainedOutputFilenameReservations.TryGetValue(reservation.Path, out Dictionary<long, OutputFilenameReservationState> owners)
                    || !owners.TryGetValue(reservation.Generation, out OutputFilenameReservationState currentState)
                    || !currentState.IsActive)
                {
                    return false;
                }
                owners[reservation.Generation] = currentState with { IsActive = false };
                return true;
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"transitioning output filename reservation '{reservation.Path}' to inactive", ex);
            return false;
        }
    }

    /// <summary>Transitions a matching failed-save or successful-deletion reservation to inactive and releases it after the safety window.</summary>
    internal static void ReleaseOutputFilenameReservationAfterDelay(OutputFilenameReservation reservation)
    {
        if (!TryTransitionOutputFilenameReservationToInactive(reservation))
        {
            return;
        }
        try
        {
            _ = Utilities.RunCheckedTask(async () =>
            {
                await Task.Delay(InactiveOutputFilenameReservationLifetime);
                RemoveOutputFilenameReservation(reservation);
            }, "output filename reservation expiry");
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"scheduling output filename reservation expiry for '{reservation.Path}'", ex);
            RemoveOutputFilenameReservation(reservation);
        }
    }

    /// <summary>Transitions a matching failed-deletion reservation to inactive without automatic expiry.</summary>
    internal static void RetainOutputFilenameReservationUntilClear(OutputFilenameReservation reservation)
    {
        TryTransitionOutputFilenameReservationToInactive(reservation);
    }

    /// <summary>Removes a pending output entry only when it still contains the captured task.</summary>
    private static void RemoveStillSavingFile(string fullPath, Task<byte[]> pendingTask)
    {
        try
        {
            StillSavingFiles.TryRemove(new KeyValuePair<string, Task<byte[]>>(fullPath, pendingTask));
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure($"removing pending output bytes for '{fullPath}'", ex);
        }
    }

    /// <summary>Clears inactive maintained owners and legacy public reservations while preserving active owners.</summary>
    internal static void ClearOutputFilenameReservations()
    {
        try
        {
            lock (OutputFilenameReservationLock)
            {
                foreach (KeyValuePair<string, Dictionary<long, OutputFilenameReservationState>> pathOwners in MaintainedOutputFilenameReservations.ToArray())
                {
                    foreach (KeyValuePair<long, OutputFilenameReservationState> owner in pathOwners.Value.ToArray())
                    {
                        if (!owner.Value.IsActive)
                        {
                            pathOwners.Value.Remove(owner.Key);
                        }
                    }
                    if (pathOwners.Value.Count == 0)
                    {
                        MaintainedOutputFilenameReservations.Remove(pathOwners.Key);
                        RecentlyBlockedFilenames.TryRemove(pathOwners.Key, out _);
                    }
                }
                foreach (string path in RecentlyBlockedFilenames.Keys)
                {
                    if (!MaintainedOutputFilenameReservations.ContainsKey(path))
                    {
                        RecentlyBlockedFilenames.TryRemove(path, out _);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogOutputCleanupFailure("clearing output filename reservations", ex);
        }
    }

    /// <summary>Save an image as this user, and returns the new URL. If user has disabled saving, returns a data URL.</summary>
    /// <returns>(User-Visible-WebPath, Local-FilePath)</returns>
    public (string, string) SaveImage(T2IEngine.ImageOutput image, int batchIndex, T2IParamInput user_input, string metadata)
    {
        return SaveImage(image, batchIndex, user_input, metadata, OutputFilenameSelectionContext.Direct);
    }

    /// <summary>Internal save route carrying only temporary privacy-safe measurement context.</summary>
    internal (string, string) SaveImage(T2IEngine.ImageOutput image, int batchIndex,
        T2IParamInput user_input, string metadata,
        OutputFilenameSelectionContext measurementContext)
    {
        OutputFilenameSelectionAttempt measurement = null;
        if (OutputFilenameSelectionMeasurement.IsEnabled)
        {
            measurement = OutputFilenameSelectionMeasurement.Begin(
                measurementContext,
                image.File,
                user_input.Get(T2IParamTypes.BatchSize, 1));
        }
        if (!User.Settings.SaveFiles)
        {
            OutputFilenameSelectionMeasurement.EmitBypass(
                measurement, "user_save_files_disabled");
            return (image.File.AsDataString(), null);
        }
        long synchronousStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
        long pathStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
        string rawImagePath = User.BuildImageOutputPath(user_input, batchIndex);
        string imagePath = rawImagePath.Replace("[number]", "1");
        string format = user_input.Get(T2IParamTypes.ImageFormat, User.Settings.FileFormat.ImageFormat);
        string extension;
        try
        {
            extension = ImageFile.ImageFormatToExtension(format);
        }
        catch (Exception ex)
        {
            Logs.Debug($"Invalid file format extension: {ex.GetType().Name}: {ex.Message}");
            extension = "jpg";
        }
        if (image.File.Type.MetaType != MediaMetaType.Image)
        {
            Logs.Verbose($"Image is type {image.File.Type} and will save with extension '{image.File.Type.Extension}'.");
            extension = image.File.Type.Extension;
        }
        string fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(User, $"{User.OutputDirectory}/{imagePath}"));
        string pathFolder = imagePath.Contains('/') ? imagePath.BeforeLast('/') : "";
        string folderRoute = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(User, $"{User.OutputDirectory}/{pathFolder}"));
        string fullPath = $"{fullPathNoExt}.{extension}";
        string root = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, User.OutputDirectory);
        if (measurement is not null)
        {
            measurement.PathResolutionMicroseconds =
                OutputFilenameSelectionMeasurement.ElapsedMicroseconds(pathStart);
            measurement.FolderDepth = pathFolder.Split(
                '/', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        bool saveFailed = false;
        long lockWaitStart = measurement is null ? 0 : OutputFilenameSelectionMeasurement.Timestamp();
        lock (User.UserLock)
        {
            long lockHoldStart = 0;
            if (measurement is not null)
            {
                measurement.UserLockWaitMicroseconds =
                    OutputFilenameSelectionMeasurement.ElapsedMicroseconds(lockWaitStart);
                lockHoldStart = OutputFilenameSelectionMeasurement.Timestamp();
            }
            try
            {
                Directory.CreateDirectory(folderRoute);
                HashSet<string> existingFiles;
                if (measurement is null)
                {
                    existingFiles = [.. Directory.EnumerateFiles(folderRoute)
                        .Select(file => file.BeforeLast('.'))];
                }
                else
                {
                    long enumerationStart = OutputFilenameSelectionMeasurement.Timestamp();
                    string[] folderFiles = [.. Directory.EnumerateFiles(folderRoute)];
                    measurement.DirectoryEnumerationMicroseconds =
                        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(enumerationStart);
                    measurement.FolderFileCount = folderFiles.Length;
                    long hashStart = OutputFilenameSelectionMeasurement.Timestamp();
                    existingFiles = [.. folderFiles.Select(file => file.BeforeLast('.'))];
                    measurement.ExtensionlessHashMicroseconds =
                        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(hashStart);
                }
                OutputFilenameReservation reservation = default;
                int num = 0;
                if (measurement is null)
                {
                    while (existingFiles.Contains(fullPathNoExt)
                        || !TryReserveOutputFilename(fullPath, out reservation, null))
                    {
                        num++;
                        imagePath = rawImagePath.Contains("[number]")
                            ? rawImagePath.Replace("[number]", $"{num}")
                            : $"{rawImagePath}-{num}";
                        fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
                            User, $"{User.OutputDirectory}/{imagePath}"));
                        fullPath = $"{fullPathNoExt}.{extension}";
                    }
                }
                else
                {
                    while (true)
                    {
                        measurement.CandidateProbeCount++;
                        long probeStart = OutputFilenameSelectionMeasurement.Timestamp();
                        bool diskCollision = existingFiles.Contains(fullPathNoExt);
                        bool reserved = !diskCollision
                            && TryReserveOutputFilename(fullPath, out reservation, measurement);
                        measurement.CandidateProbeMicroseconds +=
                            OutputFilenameSelectionMeasurement.ElapsedMicroseconds(probeStart);
                        if (reserved)
                        {
                            break;
                        }
                        num++;
                        imagePath = rawImagePath.Contains("[number]")
                            ? rawImagePath.Replace("[number]", $"{num}")
                            : $"{rawImagePath}-{num}";
                        fullPathNoExt = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(
                            User, $"{User.OutputDirectory}/{imagePath}"));
                        fullPath = $"{fullPathNoExt}.{extension}";
                    }
                    measurement.NamingCategory = num == 0
                        ? "direct"
                        : rawImagePath.Contains("[number]") ? "number_token" : "suffix";
                }
                Task<byte[]> pendingTask = null;
                try
                {
                    pendingTask = image.ActualFileTask is null ? Task.FromResult(image.File.RawData) : Task.Run(async () => (await image.ActualFileTask).RawData);
                    StillSavingFiles[fullPath] = pendingTask;
                    _ = Utilities.RunCheckedTask(async () =>
                    {
                        bool saveSucceeded = false;
                        try
                        {
                            MediaFile actualFile = image.ActualFileTask is null ? image.File : await image.ActualFileTask;
                            File.WriteAllBytes(fullPath, actualFile.RawData);
                            if ((User.Settings.FileFormat.SaveTextFileMetadata || extension == "webp" || !OutputMetadataTracker.ExtensionsWithMetadata.Contains(extension)) && !string.IsNullOrWhiteSpace(metadata))
                            {
                                if (extension == "webp" && actualFile is ImageFile imageFile && imageFile.ToIS.Frames.Count == 1)
                                {
                                    // no .json write for still-image webps
                                }
                                else
                                {
                                    File.WriteAllBytes(fullPathNoExt + ".swarm.json", metadata.EncodeUTF8());
                                }
                            }
                            OutputMetadataTracker.GetOrCreatePreviewFor(fullPath.Replace('\\', '/'));
                            OutputMetadataTracker.UpsertHistoryIndexForFile(fullPath.Replace('\\', '/'), root, User.Settings.StarNoFolders);
                            Logs.Debug($"Saved an output file as '{fullPath}'");
                            await Task.Delay(TimeSpan.FromSeconds(10)); // (Give time for WebServer to read data from cache rather than having to reload from file for first read)
                            saveSucceeded = true;
                        }
                        finally
                        {
                            RemoveStillSavingFile(fullPath, pendingTask);
                            if (saveSucceeded)
                            {
                                ReleaseOutputFilenameReservation(reservation);
                            }
                            else
                            {
                                ReleaseOutputFilenameReservationAfterDelay(reservation);
                            }
                        }
                    }, "output file save");
                }
                catch
                {
                    if (pendingTask is not null)
                    {
                        RemoveStillSavingFile(fullPath, pendingTask);
                    }
                    ReleaseOutputFilenameReservationAfterDelay(reservation);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Could not save user '{User.UserID}' image (to '{fullPath}'): error '{ex.Message}'");
                saveFailed = true;
            }
            finally
            {
                if (measurement is not null)
                {
                    measurement.UserLockHoldMicroseconds =
                        OutputFilenameSelectionMeasurement.ElapsedMicroseconds(lockHoldStart);
                }
            }
        }
        if (measurement is not null)
        {
            measurement.SynchronousSelectionMicroseconds =
                OutputFilenameSelectionMeasurement.ElapsedMicroseconds(synchronousStart);
            OutputFilenameSelectionMeasurement.EmitSelection(
                measurement, saveFailed ? "error" : "scheduled");
        }
        if (saveFailed)
        {
            return ("ERROR", null);
        }
        string prefix = Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{User.UserID}/" : "Output/";
        return ($"{prefix}{imagePath}.{extension}", fullPath);
    }

    /// <summary>Gets a hash code for this session, for C# equality comparsion.</summary>
    public override int GetHashCode()
    {
        return ID.GetHashCode();
    }

    /// <summary>Returns true if this session is the same as another.</summary>
    public override bool Equals(object obj)
    {
        return obj is Session session && Equals(session);
    }

    /// <summary>Returns true if this session is the same as another.</summary>
    public bool Equals(Session other)
    {
        return ID == other.ID;
    }

    /// <summary>Immediately interrupt any current processing on this session.</summary>
    public void Interrupt()
    {
        SessInterrupt.Cancel();
        SessInterrupt = new();
    }
}
