using EnterpriseAgent.Api.Models;

namespace EnterpriseAgent.Api.Rag;

public sealed class TextChunker
{
    public IReadOnlyCollection<PolicyChunk> Chunk(PolicyDocument document)
    {
        var chunks = new List<PolicyChunk>();
        string? currentSection = null;
        var content = new List<string>();

        foreach (var rawLine in document.Content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddChunk(chunks, document.Source, currentSection, content);
                currentSection = line[3..].Trim();
                content.Clear();
            }
            else if (currentSection is not null && line.Length > 0)
            {
                content.Add(line);
            }
        }

        AddChunk(chunks, document.Source, currentSection, content);
        return chunks;
    }

    private static void AddChunk(
        ICollection<PolicyChunk> chunks,
        string source,
        string? section,
        IReadOnlyCollection<string> lines)
    {
        if (string.IsNullOrWhiteSpace(section) || lines.Count == 0)
        {
            return;
        }

        chunks.Add(new PolicyChunk(source, section, string.Join(' ', lines)));
    }
}
