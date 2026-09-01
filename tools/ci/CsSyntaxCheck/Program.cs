// CsSyntaxCheck - parse every .cs file under a directory with Roslyn and report syntax errors.
//
// Usage: dotnet run --project tools/ci/CsSyntaxCheck -- <directory> [--lang <version>]
//
// Each file is parsed once per preprocessor profile so code inside
// #if UNITY_ANDROID / UNITY_IOS / UNITY_SENTIS branches is checked too.
// Unity 2022.3 compiles C# 9, so that is the default language version.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string root = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : ".";
var langVersion = LanguageVersion.CSharp9;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--lang" && LanguageVersionFacts.TryParse(args[i + 1], out var parsed))
        langVersion = parsed;
}

var profiles = new Dictionary<string, string[]>
{
    ["editor"] = new[] { "UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_2022_3_OR_NEWER", "UNITY_2020_1_OR_NEWER", "UNITY_INCLUDE_TESTS" },
    ["android"] = new[] { "UNITY_ANDROID", "UNITY_2022_3_OR_NEWER", "UNITY_2020_1_OR_NEWER" },
    ["android-sentis"] = new[] { "UNITY_ANDROID", "UNITY_SENTIS", "UNITY_2022_3_OR_NEWER", "UNITY_2020_1_OR_NEWER" },
    ["ios"] = new[] { "UNITY_IOS", "UNITY_2022_3_OR_NEWER", "UNITY_2020_1_OR_NEWER" },
};

var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Any(part => part is "Library" or "Temp" or "obj" or "bin"))
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToList();

if (files.Count == 0)
{
    Console.Error.WriteLine($"No .cs files found under '{root}'.");
    return 2;
}

int errorCount = 0;
foreach (var (profile, symbols) in profiles)
{
    var options = new CSharpParseOptions(langVersion, DocumentationMode.None, SourceCodeKind.Regular, symbols);
    int profileErrors = 0;

    foreach (var file in files)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, path: file);
        foreach (var diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var pos = diagnostic.Location.GetLineSpan().StartLinePosition;
            Console.WriteLine($"{file}({pos.Line + 1},{pos.Character + 1}): error {diagnostic.Id}: {diagnostic.GetMessage()} [{profile}]");
            profileErrors++;
        }
    }

    Console.WriteLine($"[{profile}] {files.Count} files, {profileErrors} syntax error(s)");
    errorCount += profileErrors;
}

Console.WriteLine(errorCount == 0
    ? $"OK: {files.Count} files parse cleanly as C# {langVersion} under {profiles.Count} profiles."
    : $"FAILED: {errorCount} syntax error(s).");
return errorCount == 0 ? 0 : 1;
