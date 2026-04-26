using System.Text.Json;
using System.Text.Json.Nodes;

namespace ABHive.Web;

public record DashboardTicketActionRequest(string ProjectName, string TicketId);

public static class DashboardTicketFileOps
{
    public static bool TryRemoveSkippedTicket(string skippedSourcePath, string ticketId, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(skippedSourcePath) || !File.Exists(skippedSourcePath))
            {
                return true;
            }

            var existing = File.ReadAllText(skippedSourcePath);
            if (string.IsNullOrWhiteSpace(existing))
            {
                return true;
            }

            var parsed = JsonNode.Parse(existing);
            if (parsed is not JsonArray array)
            {
                return true;
            }

            var changed = false;
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is JsonObject obj &&
                    obj.TryGetPropertyValue("ticket_id", out var existingIdNode) &&
                    string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
                {
                    array.RemoveAt(i);
                    changed = true;
                }
            }

            if (!changed)
            {
                return true;
            }

            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            WriteAllTextAtomic(skippedSourcePath, $"{serialized}{Environment.NewLine}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryRemoveLegacySkippedTicketFromCompleted(string completedSourcePath, string ticketId, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(completedSourcePath) || !File.Exists(completedSourcePath))
            {
                return true;
            }

            var existing = File.ReadAllText(completedSourcePath);
            if (string.IsNullOrWhiteSpace(existing))
            {
                return true;
            }

            var parsed = JsonNode.Parse(existing);
            if (parsed is not JsonArray array)
            {
                return true;
            }

            var changed = false;
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is not JsonObject obj)
                {
                    continue;
                }

                if (!obj.TryGetPropertyValue("ticket_id", out var existingIdNode) ||
                    !string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (obj.TryGetPropertyValue("skipped", out var skippedNode) &&
                    skippedNode is JsonValue skippedValue &&
                    skippedValue.TryGetValue<bool>(out var skipped) &&
                    skipped)
                {
                    array.RemoveAt(i);
                    changed = true;
                }
            }

            if (!changed)
            {
                return true;
            }

            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            WriteAllTextAtomic(completedSourcePath, $"{serialized}{Environment.NewLine}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryRemoveCompletedTicket(string completedSourcePath, string ticketId, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(completedSourcePath) || !File.Exists(completedSourcePath))
            {
                return true;
            }

            var existing = File.ReadAllText(completedSourcePath);
            if (string.IsNullOrWhiteSpace(existing))
            {
                return true;
            }

            var parsed = JsonNode.Parse(existing);
            if (parsed is not JsonArray array)
            {
                return true;
            }

            var changed = false;
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is not JsonObject obj)
                {
                    continue;
                }

                if (!obj.TryGetPropertyValue("ticket_id", out var existingIdNode) ||
                    !string.Equals(existingIdNode?.GetValue<string>()?.Trim(), ticketId, StringComparison.Ordinal))
                {
                    continue;
                }

                // Ignore legacy skipped markers here; those are handled by the cleanup helper.
                if (obj.TryGetPropertyValue("skipped", out var skippedNode) &&
                    skippedNode is JsonValue skippedValue &&
                    skippedValue.TryGetValue<bool>(out var skipped) &&
                    skipped)
                {
                    continue;
                }

                array.RemoveAt(i);
                changed = true;
            }

            if (!changed)
            {
                return true;
            }

            var serialized = array.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });

            WriteAllTextAtomic(completedSourcePath, $"{serialized}{Environment.NewLine}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}

