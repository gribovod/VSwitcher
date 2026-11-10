using VSwitcher;

try
{
var engine = new Engine(Path.Combine(AppContext.BaseDirectory, "Dictionaries"));
int checks = 0;
void Equal<T>(T expected, T actual, string name)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{name}: expected [{expected}], got [{actual}]");
}
Equal("привіт", Engine.Convert("ghbdsn", Language.English, Language.Ukrainian), "English → Ukrainian");
Equal("привет", Engine.Convert("ghbdtn", Language.English, Language.Russian), "English → Russian");
Equal("Hello, world!", Engine.Convert("Руддщб цщкдв!", Language.Russian, Language.English), "Russian → English punctuation");
Equal("Привіт!\r\nСвіт", Engine.Convert("Ghbdsn!\r\nCdsn", Language.English, Language.Ukrainian), "Case and newlines");
Equal("ієї", Engine.Convert("s']", Language.English, Language.Ukrainian), "Ukrainian keys");
foreach (var source in Enum.GetValues<Language>())
foreach (var target in Enum.GetValues<Language>())
{
    var text = source switch { Language.English => "Test ABC xyz 123\r\n🙂", Language.Ukrainian => "Тест АБВ іїєґ 123\r\n🙂", _ => "Тест АБВ ыэъё 123\r\n🙂" };
    Equal(text, Engine.Convert(Engine.Convert(text, source, target), target, source), $"Round trip {source}/{target}");
}
foreach (var (text, target) in new[] { ("ghbdsn", Language.Ukrainian), ("ghbdtn", Language.Russian), ("руддщ цщкдв", Language.English) })
{
    var chosen = engine.Automatic(text, engine.Candidates(text));
    Equal(target, chosen?.Target ?? throw new Exception("No automatic candidate: " + text), "Auto " + text);
}
foreach (var text in new[] { "123", "", "🙂", "gh", "привіт", "hello world", "мир" })
    Equal<Candidate?>(null, engine.Automatic(text, engine.Candidates(text)), "No unsafe auto: " + text);
var variants = engine.Candidates("ghbdtn");
Equal(true, variants.Any(c => c.Target == Language.Ukrainian), "Ukrainian option");
Equal(true, variants.Any(c => c.Target == Language.Russian), "Russian option");
Equal(true, variants.Any(c => c.Text == "ghbdtn"), "Original option");
Console.WriteLine($"Passed {checks} checks.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
