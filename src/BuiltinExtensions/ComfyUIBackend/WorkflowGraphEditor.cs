using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SwarmUI.Builtin_ComfyUIBackend;

/// <summary>Internal owner of low-level workflow graph editing mechanics.</summary>
internal sealed class WorkflowGraphEditor
{
    /// <summary>Public workflow-generator facade whose current graph state is authoritative.</summary>
    private readonly WorkflowGenerator Generator;

    /// <summary>Creates an editor bound to one workflow-generator facade.</summary>
    public WorkflowGraphEditor(WorkflowGenerator generator)
    {
        Generator = generator;
    }

    /// <summary>Returns true if the current workflow contains the given node ID.</summary>
    public bool HasNode(string id)
    {
        return Generator.Workflow.ContainsKey(id);
    }

    /// <summary>Gets a dynamic ID within the generator's semi-stable registration set.</summary>
    public string GetStableDynamicID(int index, int offset)
    {
        for (int i = 0; i < 99999; i++)
        {
            int id = 1000 + index + offset + i;
            string result = $"{id}";
            if (!HasNode(result))
            {
                return result;
            }
        }
        throw new Exception("Failed to find a stable dynamic ID.");
    }

    /// <summary>Creates a workflow node through a configuration action.</summary>
    public string CreateNode(string classType, Action<string, JObject> configure, string id = null)
    {
        id ??= $"{Generator.LastID++}";
        JObject obj = new() { ["class_type"] = classType };
        configure(id, obj);
        Generator.Workflow[id] = obj;
        return id;
    }

    /// <summary>Creates or reuses a workflow node with the given input data.</summary>
    public string CreateNode(string classType, JObject input, string id = null, bool idMandatory = true)
    {
        string lookup = $"__generic_node__{classType}___{input}";
        if ((id is null || !idMandatory) && Generator.NodeHelpers.TryGetValue(lookup, out string existingNode))
        {
            return existingNode;
        }
        string result = CreateNode(classType, (_, n) => n["inputs"] = input, id);
        Generator.NodeHelpers[lookup] = result;
        return result;
    }

    /// <summary>Returns an array of all nodes currently in the workflow with a given class_type.</summary>
    public JProperty[] NodesOfClass(string classType)
    {
        return [.. Generator.Workflow.Properties().Where(p => $"{p.Value["class_type"]}" == classType)];
    }

    /// <summary>Returns an array of all nodes currently in the workflow with a given class_type.</summary>
    public JProperty[] NodesOfClasses(HashSet<string> classTypes)
    {
        return [.. Generator.Workflow.Properties().Where(p => classTypes.Contains($"{p.Value["class_type"]}"))];
    }

    /// <summary>Runs an action against all nodes of a given class_type.</summary>
    /// <param name="classType">The class_type to target.</param>
    /// <param name="action">The action(NodeID, JObject Data) to run against the node.</param>
    public void RunOnNodesOfClass(string classType, Action<string, JObject> action)
    {
        WorkflowCleanupCostMeasurement.Attempt attempt = Generator.WorkflowCleanupMeasurementAttempt;
        if (attempt is null)
        {
            foreach (JProperty property in NodesOfClass(classType))
            {
                action(property.Name, property.Value as JObject);
            }
            return;
        }
        WorkflowCleanupCostMeasurement.ScanStart scan = attempt.BeginScan();
        long examined = 0;
        long matched = 0;
        long callbacks = 0;
        try
        {
            JProperty[] properties = [.. Generator.Workflow.Properties().Where(p =>
            {
                examined++;
                return $"{p.Value["class_type"]}" == classType;
            })];
            matched = properties.Length;
            foreach (JProperty property in properties)
            {
                callbacks++;
                action(property.Name, property.Value as JObject);
            }
        }
        finally
        {
            attempt.CompleteScan(scan, examined, matched, callbacks);
        }
    }

    /// <summary>Replace all instances of <paramref name="oldNode"/> with <paramref name="newNode"/> in node input connections.</summary>
    public void ReplaceNodeConnection(JArray oldNode, JArray newNode)
    {
        WorkflowCleanupCostMeasurement.Attempt attempt = Generator.WorkflowCleanupMeasurementAttempt;
        if (attempt is null)
        {
            string target0 = $"{oldNode[0]}", target1 = $"{oldNode[1]}";
            foreach (JObject node in Generator.Workflow.Values().Cast<JObject>())
            {
                JObject inputs = node["inputs"] as JObject;
                foreach (JProperty property in inputs.Properties().ToArray())
                {
                    if (property.Value is JArray jarr && jarr.Count == 2 && $"{jarr[0]}" == target0 && $"{jarr[1]}" == target1)
                    {
                        inputs[property.Name] = newNode;
                    }
                }
            }
            return;
        }
        WorkflowCleanupCostMeasurement.OperationStart start = attempt.BeginOperation();
        long nodes = 0;
        long directInputs = 0;
        long matches = 0;
        long assignments = 0;
        try
        {
            string target0 = $"{oldNode[0]}", target1 = $"{oldNode[1]}";
            foreach (JObject node in Generator.Workflow.Values().Cast<JObject>())
            {
                nodes++;
                JObject inputs = node["inputs"] as JObject;
                foreach (JProperty property in inputs.Properties().ToArray())
                {
                    directInputs++;
                    if (property.Value is JArray jarr && jarr.Count == 2 && $"{jarr[0]}" == target0 && $"{jarr[1]}" == target1)
                    {
                        matches++;
                        inputs[property.Name] = newNode;
                        assignments++;
                    }
                }
            }
        }
        finally
        {
            attempt.CompleteReplacement(start, nodes, directInputs, matches, assignments);
        }
    }

    /// <summary>Returns whether a node output has an outbound connection in the current cached index.</summary>
    public bool NodeIsConnectedAnywhere(string nodeId, int ind = -1, string exclude = null)
    {
        if (Generator.UsedInputs is null)
        {
            WorkflowCleanupCostMeasurement.Attempt attempt = Generator.WorkflowCleanupMeasurementAttempt;
            if (attempt is null)
            {
                Generator.UsedInputs = [];
                foreach (JProperty node in Generator.Workflow.Properties())
                {
                    if (node.Name == exclude)
                    {
                        continue;
                    }
                    JObject inputs = node.Value["inputs"] as JObject;
                    foreach (JProperty property in inputs.Properties().ToArray())
                    {
                        if (property.Value is JArray jarr && jarr.Count == 2)
                        {
                            Generator.UsedInputs.Add($"{jarr[0]}:-1");
                            Generator.UsedInputs.Add($"{jarr[0]}:{jarr[1]}");
                        }
                    }
                }
            }
            else
            {
                WorkflowCleanupCostMeasurement.OperationStart start = attempt.BeginOperation();
                long nodes = 0;
                long directInputs = 0;
                try
                {
                    Generator.UsedInputs = [];
                    foreach (JProperty node in Generator.Workflow.Properties())
                    {
                        nodes++;
                        if (node.Name == exclude)
                        {
                            continue;
                        }
                        JObject inputs = node.Value["inputs"] as JObject;
                        foreach (JProperty property in inputs.Properties().ToArray())
                        {
                            directInputs++;
                            if (property.Value is JArray jarr && jarr.Count == 2)
                            {
                                Generator.UsedInputs.Add($"{jarr[0]}:-1");
                                Generator.UsedInputs.Add($"{jarr[0]}:{jarr[1]}");
                            }
                        }
                    }
                }
                finally
                {
                    attempt.CompleteConnectivity(start, nodes, directInputs);
                }
            }
        }
        return Generator.UsedInputs.Contains($"{nodeId}:{ind}");
    }

    /// <summary>Removes a class of nodes if they are not connected to anything.</summary>
    public void RemoveClassIfUnused(string classType)
    {
        Generator.UsedInputs = null;
        RunOnNodesOfClass(classType, (id, data) =>
        {
            if (!NodeIsConnectedAnywhere(id))
            {
                Generator.Workflow.Remove(id);
            }
        });
    }

    /// <summary>Removes a set of classes of nodes if they are not connected to anything.</summary>
    public void RemoveClassesIfUnused(HashSet<string> classTypes)
    {
        WorkflowCleanupCostMeasurement.Attempt attempt = Generator.WorkflowCleanupMeasurementAttempt;
        if (attempt is null)
        {
            bool run = true;
            while (run)
            {
                Generator.UsedInputs = null;
                run = false;
                foreach (JProperty property in NodesOfClasses(classTypes))
                {
                    if (!NodeIsConnectedAnywhere(property.Name))
                    {
                        Generator.Workflow.Remove(property.Name);
                        run = true;
                    }
                }
            }
            return;
        }
        WorkflowCleanupCostMeasurement.OperationStart start = attempt.BeginOperation();
        long passes = 0;
        long candidates = 0;
        long removals = 0;
        try
        {
            bool run = true;
            while (run)
            {
                passes++;
                Generator.UsedInputs = null;
                run = false;
                JProperty[] properties = NodesOfClasses(classTypes);
                candidates += properties.Length;
                foreach (JProperty property in properties)
                {
                    if (!NodeIsConnectedAnywhere(property.Name))
                    {
                        Generator.Workflow.Remove(property.Name);
                        removals++;
                        run = true;
                    }
                }
            }
        }
        finally
        {
            attempt.CompleteFixedPoint(start, passes, candidates, removals);
        }
    }
}
