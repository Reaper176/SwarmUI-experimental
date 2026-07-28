using System;
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
}
