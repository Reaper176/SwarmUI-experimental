using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using System.IO;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Coordinates access to stored ComfyUI workflows.</summary>
public static class ComfyWorkflowStore
{
    /// <summary>Synchronizes workflow inventory and read operations.</summary>
    private static readonly object WorkflowLock = new();

    /// <summary>Loads the available workflow files from the extension directory.</summary>
    public static void LoadWorkflowFiles(string extensionFilePath)
    {
        lock (WorkflowLock)
        {
            Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows");
            Directory.CreateDirectory($"{extensionFilePath}CustomWorkflows/Examples");
            string[] getCustomFlows(string path) => [.. Directory.EnumerateFiles($"{extensionFilePath}/{path}", "*.*", new EnumerationOptions() { RecurseSubdirectories = true }).Select(f => f.Replace('\\', '/').After($"/{path}/")).Order()];
            ComfyUIBackendExtension.ExampleWorkflowNames = getCustomFlows("ExampleWorkflows");
            string[] customFlows = getCustomFlows("CustomWorkflows");
            bool anyCopied = false;
            foreach (string workflow in ComfyUIBackendExtension.ExampleWorkflowNames.Where(f => f.EndsWith(".json")))
            {
                if (!customFlows.Contains($"Examples/{workflow}") && !customFlows.Contains($"Examples/{workflow}.deleted"))
                {
                    File.Copy($"{extensionFilePath}ExampleWorkflows/{workflow}", $"{extensionFilePath}CustomWorkflows/Examples/{workflow}");
                    anyCopied = true;
                }
            }
            if (anyCopied)
            {
                customFlows = getCustomFlows("CustomWorkflows");
            }
            ComfyUIBackendExtension.CustomWorkflows.Clear();
            foreach (string workflow in customFlows.Where(f => f.EndsWith(".json")))
            {
                ComfyUIBackendExtension.CustomWorkflows.TryAdd(workflow.BeforeLast('.'), null);
            }
        }
    }

    /// <summary>Gets a workflow by its stored name.</summary>
    public static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByName(string name)
    {
        lock (WorkflowLock)
        {
            return GetWorkflowByNameLocked(name);
        }
    }

    /// <summary>Gets a workflow by its stored name while the workflow lock is held.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow GetWorkflowByNameLocked(string name)
    {
        if (!ComfyUIBackendExtension.CustomWorkflows.TryGetValue(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow))
        {
            return null;
        }
        if (workflow is not null)
        {
            return workflow;
        }
        string path = $"{ComfyUIBackendExtension.Folder}/CustomWorkflows/{name}.json";
        if (!File.Exists(path))
        {
            ComfyUIBackendExtension.CustomWorkflows.TryRemove(name, out _);
            return null;
        }
        try
        {
            JObject json = ComfySubmittedJson.ParseObject(File.ReadAllText(path));
            string getStringFor(string key)
            {
                if (!json.TryGetValue(key, out JToken data))
                {
                    return null;
                }
                if (data.Type == JTokenType.String)
                {
                    return data.ToString();
                }
                return data.ToString(Formatting.None);
            }
            string workflowData = getStringFor("workflow");
            string prompt = getStringFor("prompt");
            string customParams = getStringFor("custom_params");
            string paramValues = getStringFor("param_values");
            string image = getStringFor("image") ?? "/imgs/model_placeholder.jpg";
            string description = getStringFor("description");
            bool enableInSimple = json.TryGetValue("enable_in_simple", out JToken enableInSimpleTok) && enableInSimpleTok.ToObject<bool>();
            workflow = new(name, workflowData, prompt, customParams, paramValues, image, description, enableInSimple);
            ComfyUIBackendExtension.CustomWorkflows[name] = workflow;
            return workflow;
        }
        catch (Exception)
        {
            Logs.Error("Error loading ComfyUI custom workflow (submitted content redacted).");
            return null;
        }
    }

    /// <summary>Gets a hydrated snapshot of all currently known workflows.</summary>
    public static List<ComfyUIBackendExtension.ComfyCustomWorkflow> GetWorkflowSnapshot()
    {
        lock (WorkflowLock)
        {
            List<string> names = ComfyUIBackendExtension.CustomWorkflows.Keys.ToList();
            List<ComfyUIBackendExtension.ComfyCustomWorkflow> workflows = [];
            foreach (string name in names)
            {
                ComfyUIBackendExtension.ComfyCustomWorkflow workflow = GetWorkflowByNameLocked(name);
                if (workflow is not null)
                {
                    workflows.Add(workflow);
                }
            }
            return workflows;
        }
    }

    /// <summary>Gets a sorted snapshot of all known workflow names.</summary>
    public static string[] GetWorkflowNames()
    {
        lock (WorkflowLock)
        {
            return [.. ComfyUIBackendExtension.CustomWorkflows.Keys.Order()];
        }
    }

    /// <summary>Gets a workflow prompt while distinguishing unknown names from unloaded workflows.</summary>
    public static bool TryGetWorkflowParameterPrompt(string name, out string prompt)
    {
        lock (WorkflowLock)
        {
            if (!ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name))
            {
                prompt = null;
                return false;
            }
            prompt = GetWorkflowByNameLocked(name)?.Prompt;
            return true;
        }
    }
}
