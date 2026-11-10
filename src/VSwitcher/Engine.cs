using System.Text;
using System.Text.RegularExpressions;

namespace VSwitcher;

public enum Language { English, Ukrainian, Russian }
public record Candidate(Language Source, Language Target, string Text, double Score);

public sealed class Engine
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(char, Language, Language), char> Characters = new();
    private readonly Dictionary<Language, HashSet<string>> words = new();
    public Engine(string directory)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var (language, file) in new[] { (Language.English, "en_en_US"), (Language.Ukrainian, "uk_UA_uk_UA"), (Language.Russian, "ru_RU_ru_RU") })
        {
            var aff = File.ReadLines(Path.Combine(directory, file + ".aff")).First(x => x.StartsWith("SET "))[4..].Trim();
            var encoding = Encoding.GetEncoding(aff);
            words[language] = File.ReadLines(Path.Combine(directory, file + ".dic"), encoding).Skip(1)
                .Select(x => x.Split('/', '\t', ' ')[0].ToLowerInvariant()).ToHashSet();
        }
    }
    public static string Name(Language l) => l switch { Language.Ukrainian => "Українська", Language.Russian => "Російська", _ => "English" };
    public static string Convert(string text, Language source, Language target)
    {
        if (source == target) return text;
        return new string(text.Select(c => Characters.GetOrAdd((c, source, target), k => Native.Translate(k.Item1, k.Item2, k.Item3))).ToArray());
    }
    public double Score(string text, Language language)
    {
        var tokens = Regex.Matches(text.ToLowerInvariant(), @"\p{L}+(?:['’]\p{L}+)*").Select(x => x.Value).ToArray();
        if (tokens.Length == 0) return 0;
        double total = tokens.Sum(x => Math.Min(x.Length, 12));
        return tokens.Where(x => words[language].Contains(x.Replace('’', '\''))).Sum(x => Math.Min(x.Length, 12)) / total;
    }
    public List<Candidate> Candidates(string text)
    {
        bool cyrillic = text.Any(c => c is >= '\u0400' and <= '\u04ff');
        var sources = cyrillic ? new[] { Language.Ukrainian, Language.Russian } : new[] { Language.English };
        return sources.SelectMany(s => Enum.GetValues<Language>().Select(t => new Candidate(s, t, Convert(text, s, t), 0)))
            .Select(c => c with { Score = Score(c.Text, c.Target) })
            .DistinctBy(c => (c.Target, c.Text)).OrderByDescending(c => c.Score).ToList();
    }
    public Candidate? Automatic(string original, List<Candidate> candidates)
    {
        var best = candidates[0];
        var runner = candidates.Skip(1).FirstOrDefault(c => c.Target != best.Target || c.Text != best.Text);
        var letters = best.Text.Count(char.IsLetter);
        return letters >= 5 && best.Text != original && best.Score >= .80 && best.Score - (runner?.Score ?? 0) >= .25 ? best : null;
    }
}
