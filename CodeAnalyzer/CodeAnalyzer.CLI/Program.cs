using System;
using System.CommandLine;
using CodeAnalyzer.Analyzers;
using CodeAnalyzer.Git;
using System.Text.Json;
using CodeAnalyzer.Core.Models;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace CodeAnalyzer.CLI;

class Program
{
    private static readonly Random _random = new Random();
    private static ConsoleColor _originalColor;
    private static bool _useMatrixEffect = true;
    private static readonly bool _isInteractive = !Console.IsOutputRedirected && !Console.IsInputRedirected;

    static async Task<int> Main(string[] args)
    {
        _originalColor = Console.ForegroundColor;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Set console to fullscreen for maximum vibe
        try { Console.SetWindowSize(Math.Min(150, Console.LargestWindowWidth), Math.Min(50, Console.LargestWindowHeight)); } catch { }

        ShowBootSequence();

        var rootCommand = new RootCommand("Code Analyzer - Analyzes C# code for quality issues");

        // File analysis command
        var fileCommand = new Command("file", "Analyze a single file");
        var filePathOption = new Option<string>("--file", "Path to the C# file") { IsRequired = true };
        fileCommand.AddOption(filePathOption);
        fileCommand.SetHandler(async (filePath) => await AnalyzeFile(filePath), filePathOption);
        rootCommand.AddCommand(fileCommand);

        // Git diff analysis command
        var diffCommand = new Command("diff", "Analyze git diff");
        var repoPathOption = new Option<string>("--repo", "Path to git repository") { IsRequired = true };
        var branchOption = new Option<string>("--branch", "Branch to compare against") { IsRequired = false };
        diffCommand.AddOption(repoPathOption);
        diffCommand.AddOption(branchOption);
        diffCommand.SetHandler(async (repoPath, branch) => await AnalyzeDiff(repoPath, branch),
            repoPathOption, branchOption);
        rootCommand.AddCommand(diffCommand);

        // PR analysis command
        var prCommand = new Command("pr", "Analyze GitHub pull request");
        var repoUrlOption = new Option<string>("--repo-url", "Git repository URL") { IsRequired = true };
        var prNumberOption = new Option<int>("--pr-number", "Pull request number") { IsRequired = true };
        var tokenOption = new Option<string>("--token", "GitHub token (optional)") { IsRequired = false };
        prCommand.AddOption(repoUrlOption);
        prCommand.AddOption(prNumberOption);
        prCommand.AddOption(tokenOption);
        prCommand.SetHandler(async (repoUrl, prNumber, token) =>
            await AnalyzePR(repoUrl, prNumber, token), repoUrlOption, prNumberOption, tokenOption);
        rootCommand.AddCommand(prCommand);

        // Directory analysis command
        var dirCommand = new Command("dir", "Analyze all C# files in a directory");
        var dirPathOption = new Option<string>("--directory", "Directory path") { IsRequired = true };
        dirCommand.AddOption(dirPathOption);
        dirCommand.SetHandler(async (dirPath) => await AnalyzeDirectory(dirPath), dirPathOption);
        rootCommand.AddCommand(dirCommand);

        // Matrix effect toggle
        var matrixCommand = new Command("matrix", "Toggle matrix effect");
        matrixCommand.SetHandler(() => { _useMatrixEffect = !_useMatrixEffect; Console.WriteLine($"Matrix effect: {(_useMatrixEffect ? "ON" : "OFF")}"); });
        rootCommand.AddCommand(matrixCommand);

        // Refactor commands -----------------------------------------------
        var refactorCommand = new Command("refactor", "Inspect and apply automated refactors");

        var refFileOption = new Option<string>("--file", "Path to the C# file") { IsRequired = true };

        // refactor list --file X
        var listCmd = new Command("list", "List available refactors for a file");
        listCmd.AddOption(refFileOption);
        listCmd.SetHandler(async (file) => await ListRefactors(file), refFileOption);
        refactorCommand.AddCommand(listCmd);

        // refactor preview --file X --id Y [--with-deps]
        var previewCmd = new Command("preview", "Preview the result of applying a refactor");
        var idOption = new Option<string>("--id", "Refactor id (from `refactor list`)") { IsRequired = true };
        var withDepsOption = new Option<bool>("--with-deps", () => true, "Apply declared dependencies first");
        previewCmd.AddOption(refFileOption);
        previewCmd.AddOption(idOption);
        previewCmd.AddOption(withDepsOption);
        previewCmd.SetHandler(async (file, id, withDeps) => await PreviewRefactor(file, id, withDeps),
            refFileOption, idOption, withDepsOption);
        refactorCommand.AddCommand(previewCmd);

        // refactor apply --file X (--id Y | --all) [--with-deps]
        var applyCmd = new Command("apply", "Apply a refactor and write the file");
        var applyIdOption = new Option<string>("--id", "Refactor id (from `refactor list`). Omit to use --all.");
        var allOption = new Option<bool>("--all", () => false, "Apply every available refactor in the file");
        applyCmd.AddOption(refFileOption);
        applyCmd.AddOption(applyIdOption);
        applyCmd.AddOption(allOption);
        applyCmd.AddOption(withDepsOption);
        applyCmd.SetHandler(async (file, id, all, withDeps) => await ApplyRefactor(file, id, all, withDeps),
            refFileOption, applyIdOption, allOption, withDepsOption);
        refactorCommand.AddCommand(applyCmd);

        rootCommand.AddCommand(refactorCommand);

        ShowPrompt();
        return await rootCommand.InvokeAsync(args);
    }

    static void ShowBootSequence()
    {
        try { Console.Clear(); } catch { /* not a TTY */ }
        ShowAsciiArt();
        TypewriterEffect("INITIALIZING CODE ANALYZER v3.1.4", 2000, ConsoleColor.Cyan);
        Thread.Sleep(500);

        var bootSteps = new[]
        {
            "Loading Roslyn Compiler Platform...",
            "Initializing Semantic Models...",
            "Calibrating SOLID Detectors...",
            "Loading Git Integration Module...",
            "Preparing Neural Network...",
            "System Ready."
        };

        foreach (var step in bootSteps)
        {
            TypewriterEffect(step, 100, ConsoleColor.DarkGreen);
            Thread.Sleep(200);
        }

        Console.WriteLine();
        ShowMatrixRain(3);
    }

    static void ShowAsciiArt()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        var ascii = @"
    ╔═══════════════════════════════════════════════════════════════════╗
    ║  ██████╗ ██████╗ ██████╗ ███████╗     █████╗ ███╗   ██╗ █████╗ ██╗  ║
    ║ ██╔════╝██╔═══██╗██╔══██╗██╔════╝    ██╔══██╗████╗  ██║██╔══██╗██║  ║
    ║ ██║     ██║   ██║██║  ██║█████╗      ██║  ██║██╔██╗ ██║███████║██║  ║
    ║ ██║     ██║   ██║██║  ██║██╔══╝      ██║  ██║██║╚██╗██║██╔══██║██║  ║
    ║ ╚██████╗╚██████╔╝██████╔╝███████╗    ╚█████╔╝██║ ╚████║██║  ██║███████╗
    ║  ╚═════╝ ╚═════╝ ╚═════╝ ╚══════╝     ╚════╝ ╚═╝  ╚═══╝╚═╝  ╚═╝╚══════╝
    ║                                                                       ║
    ║           ███████╗██╗   ██╗███████╗████████╗███████╗███╗   ███╗      ║
    ║           ██╔════╝╚██╗ ██╔╝██╔════╝╚══██╔══╝██╔════╝████╗ ████║      ║
    ║           █████╗   ╚████╔╝ ███████╗   ██║   █████╗  ██╔████╔██║      ║
    ║           ██╔══╝    ╚██╔╝  ╚════██║   ██║   ██╔══╝  ██║╚██╔╝██║      ║
    ║           ███████╗   ██║   ███████║   ██║   ███████╗██║ ╚═╝ ██║      ║
    ║           ╚══════╝   ╚═╝   ╚══════╝   ╚═╝   ╚══════╝╚═╝     ╚═╝      ║
    ╚═══════════════════════════════════════════════════════════════════╝";

        Console.WriteLine(ascii);
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("    ╔═══════════════════════════════════════════════════════════════════╗");
        Console.Write("    ║");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("          ADVANCED CODE ANALYSIS SUITE - ENTER THE MATRIX                 ");
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("║");
        Console.WriteLine("    ╚═══════════════════════════════════════════════════════════════════╝");
        Console.ForegroundColor = ConsoleColor.Green;
    }

    static void TypewriterEffect(string text, int delay, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        foreach (char c in text)
        {
            Console.Write(c);
            Thread.Sleep(delay / text.Length);
        }
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
    }

    static void ShowMatrixRain(int durationSeconds)
    {
        if (!_useMatrixEffect || !_isInteractive) return;

        var originalLeft = Console.CursorLeft;
        var originalTop = Console.CursorTop;

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        var endTime = DateTime.Now.AddSeconds(durationSeconds);

        while (DateTime.Now < endTime)
        {
            var width = Console.WindowWidth;
            for (int i = 0; i < width / 4; i++)
            {
                Console.SetCursorPosition(_random.Next(width), _random.Next(Console.WindowHeight - 5));
                Console.Write((char)_random.Next(33, 126));
                Thread.Sleep(10);
            }
        }

        Console.SetCursorPosition(originalLeft, originalTop);
        Console.ForegroundColor = ConsoleColor.Green;
    }

    static void ShowPrompt()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────┐");
        Console.Write("│  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("AVAILABLE COMMANDS");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("                                                  │");
        Console.WriteLine("├─────────────────────────────────────────────────────────────────┤");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("│  file <--file path>     - Analyze single C# file               │");
        Console.WriteLine("│  diff <--repo path>     - Analyze git diff changes             │");
        Console.WriteLine("│  pr <--repo-url> <--pr> - Analyze GitHub pull request          │");
        Console.WriteLine("│  dir <--directory>      - Analyze entire directory             │");
        Console.WriteLine("│  refactor list          - List automated refactors for a file  │");
        Console.WriteLine("│  refactor preview       - Preview a refactor as a diff         │");
        Console.WriteLine("│  refactor apply         - Apply refactor(s), incl. dependencies│");
        Console.WriteLine("│  matrix                 - Toggle matrix rain effect            │");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("│                                                                 │");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("│  Example: file --file C:\\Projects\\App\\Program.cs              │");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\n> ");
    }

    static async Task AnalyzeFile(string filePath)
    {
        ShowLoadingAnimation("INITIALIZING DEEP SCAN");

        var analyzer = new CodeAnalyzerEngine();
        var result = await analyzer.AnalyzeFileAsync(filePath);

        ShowAnalysisHeader("SINGLE FILE ANALYSIS");
        PrintResults(new[] { result });
    }

    static async Task AnalyzeDiff(string repoPath, string branch)
    {
        ShowLoadingAnimation("ANALYZING GIT DIFF STREAM");

        var gitService = new GitService();
        var results = await gitService.AnalyzeGitDiffAsync(repoPath, branch);

        ShowAnalysisHeader($"GIT DIFF ANALYSIS {(string.IsNullOrEmpty(branch) ? "(CURRENT BRANCH)" : $"(VS {branch})")}");
        PrintResults(results);
    }

    static async Task AnalyzePR(string repoUrl, int prNumber, string token)
    {
        ShowLoadingAnimation($"FETCHING PR #{prNumber} FROM GITHUB");

        var gitService = new GitService();
        var results = await gitService.AnalyzePullRequestAsync(repoUrl, prNumber, token);

        ShowAnalysisHeader($"PULL REQUEST #{prNumber} ANALYSIS");
        PrintResults(results);
    }

    static async Task AnalyzeDirectory(string directoryPath)
    {
        ShowLoadingAnimation("SCANNING DIRECTORY STRUCTURE");

        var files = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
        var results = new List<AnalysisResult>();
        var analyzer = new CodeAnalyzerEngine();

        var progress = 0;
        foreach (var file in files)
        {
            UpdateProgress(++progress, files.Length, $"ANALYZING {Path.GetFileName(file)}");
            var result = await analyzer.AnalyzeFileAsync(file);
            results.Add(result);
        }

        ShowAnalysisHeader($"DIRECTORY ANALYSIS - {Path.GetFileName(directoryPath)}");
        PrintResults(results);

        // Print detailed summary with hacker stats
        var totalIssues = results.Sum(r => r.Issues.Count);
        var avgScore = results.Average(r => r.OverallScore);
        var criticalIssues = results.Sum(r => r.Issues.Count(i => i.Severity == "Error"));
        var warnings = results.Sum(r => r.Issues.Count(i => i.Severity == "Warning"));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                         SYSTEM SUMMARY                            ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════╣");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"║  📊 FILES ANALYZED:        {results.Count,35} ║");
        Console.WriteLine($"║  🐛 TOTAL ISSUES:          {totalIssues,35} ║");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"║  💀 CRITICAL ISSUES:       {criticalIssues,35} ║");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"║  ⚠️  WARNINGS:              {warnings,35} ║");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"║  🎯 AVG QUALITY SCORE:     {avgScore,35:F2}/100 ║");
        Console.ForegroundColor = ConsoleColor.Cyan;

        var grade = avgScore >= 90 ? "S" : avgScore >= 80 ? "A" : avgScore >= 70 ? "B" : avgScore >= 60 ? "C" : "F";
        Console.WriteLine($"║  🏆 CODE GRADE:            {grade,35} ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");

        if (avgScore < 70)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n⚠️  WARNING: CODE QUALITY BELOW ACCEPTABLE THRESHOLD");
            Console.WriteLine("    RECOMMENDED ACTIONS: REVIEW SOLID VIOLATIONS & REDUCE COMPLEXITY");
        }
    }

    static void ShowLoadingAnimation(string message)
    {
        if (!_isInteractive) { Console.WriteLine($"[{message}]"); return; }
        try { Console.Clear(); } catch { }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n┌─────────────────────────────────────────────────────────────────┐");
        Console.Write("│  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("CODE ANALYZER v3.1.4");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("                                                │");
        Console.WriteLine("├─────────────────────────────────────────────────────────────────┤");
        Console.Write("│  ");

        var spinner = new[] { '|', '/', '-', '\\' };
        var spinnerIndex = 0;

        for (int i = 0; i <= 100; i++)
        {
            Console.SetCursorPosition(2, Console.CursorTop);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"[{spinner[spinnerIndex]}] {message}... {i}%");
            spinnerIndex = (spinnerIndex + 1) % spinner.Length;

            // Simulate progress
            var delay = _random.Next(5, 20);
            Thread.Sleep(delay);
        }

        Console.SetCursorPosition(0, Console.CursorTop);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n│  ✔ {message} COMPLETE");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
        Thread.Sleep(500);
    }

    static void UpdateProgress(int current, int total, string currentFile)
    {
        var percentage = (current * 100) / total;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.Write($"\r├─ SCAN PROGRESS: [{new string('=', percentage / 2)}{new string(' ', 50 - percentage / 2)}] {percentage}%");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" ─ {currentFile.PadRight(40)}");
    }

    static void ShowAnalysisHeader(string analysisType)
    {
        try { Console.Clear(); } catch { }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  {analysisType.PadRight(63)} ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════╣");

        // Show timestamp
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"║  TIMESTAMP: {DateTime.Now:yyyy-MM-dd HH:mm:ss}                                  ║");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    static void PrintResults(IEnumerable<AnalysisResult> results)
    {
        var fileCount = 0;
        foreach (var result in results)
        {
            fileCount++;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n┌─ [{fileCount}] ──────────────────────────────────────────────────────────");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  📁 {Path.GetFileName(result.FilePath)}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"│  📂 {result.FilePath}");

            // Score with color gradient
            Console.Write("│  ");
            if (result.OverallScore >= 80) Console.ForegroundColor = ConsoleColor.Green;
            else if (result.OverallScore >= 60) Console.ForegroundColor = ConsoleColor.Yellow;
            else Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"📊 SCORE: {result.OverallScore:F2}/100");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"│  🔄 CYCLOMATIC COMPLEXITY: {result.Complexity.CyclomaticComplexity}");
            Console.WriteLine($"│  📏 MAX NESTING DEPTH: {result.Complexity.MaxNestingDepth}");
            Console.WriteLine($"│  📝 LINES OF CODE: {result.Complexity.LineCount}");
            Console.WriteLine($"│  🏛️  CLASSES: {result.Complexity.ClassCount}");
            Console.WriteLine($"│  🔧 METHODS: {result.Complexity.MethodCount}");

            if (result.Issues.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"│\n│  ⚠️  VULNERABILITIES DETECTED: {result.Issues.Count}");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("│  ─────────────────────────────────────────────────────────────");

                var grouped = result.Issues.GroupBy(i => i.Type);
                foreach (var group in grouped)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"│\n│  ▸ {group.Key} VIOLATIONS:");

                    foreach (var issue in group)
                    {
                        var icon = issue.Severity == "Error" ? "💀" :
                                  issue.Severity == "Warning" ? "⚠️" : "ℹ️";

                        var color = issue.Severity == "Error" ? ConsoleColor.Red :
                                   issue.Severity == "Warning" ? ConsoleColor.Yellow :
                                   ConsoleColor.Gray;

                        Console.ForegroundColor = color;
                        Console.WriteLine($"│    {icon} LINE {issue.LineNumber,4}: {issue.Message}");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"│       └─ RULE: {issue.RuleName}");
                        if (issue.Suggestion != null)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"│       💡 FIX : {issue.Suggestion.Title}");
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"│              run: refactor apply --file <path> --id {issue.Suggestion.Id}");
                        }
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"│\n│  ✨ NO ISSUES FOUND - CLEAN CODE DETECTED ✨");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("└───────────────────────────────────────────────────────────────────");
        }

        Console.ForegroundColor = _originalColor;
        ShowPrompt();
    }

    // ---------------------------------------------------------------
    //  Refactor handlers
    // ---------------------------------------------------------------

    static async Task ListRefactors(string filePath)
    {
        var engine = new RefactorEngine();
        var refactors = await engine.ListAsync(filePath);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n┌─ AVAILABLE REFACTORS ─ {Path.GetFileName(filePath)}");
        if (refactors.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("│  ✨ No automated refactors needed.");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("└─────────────────────────────────────────────────────────────────");
            return;
        }

        foreach (var r in refactors)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  [{r.Id}]");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"│    KIND  : {r.Kind}");
            Console.WriteLine($"│    LINE  : {r.LineNumber}");
            Console.WriteLine($"│    TITLE : {r.Title}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"│    DESC  : {r.Description}");
            if (r.Dependencies.Count > 0)
                Console.WriteLine($"│    DEPS  : {string.Join(", ", r.Dependencies)}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("│");
        }
        Console.WriteLine("└─────────────────────────────────────────────────────────────────");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Run `refactor preview --file <path> --id <id>` to see the diff.");
        Console.WriteLine($"  Run `refactor apply   --file <path> --id <id>` to apply.");
        Console.ForegroundColor = _originalColor;
    }

    static async Task PreviewRefactor(string filePath, string id, bool withDeps)
    {
        var engine = new RefactorEngine();
        var result = await engine.ApplyAsync(filePath, new[] { id }, withDeps, writeToDisk: false);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n┌─ REFACTOR PREVIEW ─ {Path.GetFileName(filePath)}");
        if (!result.Changed)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("│  No changes produced (refactor not found, or already applied).");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"│  Applied refactors: {string.Join(", ", result.AppliedRefactorIds)}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("├─ DIFF ─────────────────────────────────────────────────────────");
            PrintUnifiedDiff(result.OriginalText, result.NewText);
        }
        if (result.SkippedReasons.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("├─ SKIPPED ──────────────────────────────────────────────────────");
            foreach (var s in result.SkippedReasons) Console.WriteLine($"│  • {s}");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("└─────────────────────────────────────────────────────────────────");
        Console.ForegroundColor = _originalColor;
    }

    static async Task ApplyRefactor(string filePath, string id, bool all, bool withDeps)
    {
        var engine = new RefactorEngine();

        IEnumerable<string> ids;
        if (all)
        {
            var available = await engine.ListAsync(filePath);
            ids = available.Select(r => r.Id);
        }
        else if (!string.IsNullOrWhiteSpace(id))
        {
            ids = new[] { id };
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Provide either --id <id> or --all.");
            Console.ForegroundColor = _originalColor;
            return;
        }

        var result = await engine.ApplyAsync(filePath, ids, withDeps, writeToDisk: true);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n┌─ REFACTOR APPLY ─ {Path.GetFileName(filePath)}");
        if (!result.Changed)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("│  No changes were written to disk.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"│  ✔ File updated.");
            Console.WriteLine($"│  Applied: {string.Join(", ", result.AppliedRefactorIds)}");
        }
        if (result.SkippedReasons.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("├─ SKIPPED ──────────────────────────────────────────────────────");
            foreach (var s in result.SkippedReasons) Console.WriteLine($"│  • {s}");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("└─────────────────────────────────────────────────────────────────");
        Console.ForegroundColor = _originalColor;
    }

    /// <summary>Crude unified-diff printer — line-level only, sufficient for previews.</summary>
    static void PrintUnifiedDiff(string before, string after)
    {
        var a = before.Replace("\r\n", "\n").Split('\n');
        var b = after.Replace("\r\n", "\n").Split('\n');

        // LCS table
        int n = a.Length, m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine($"  {a[x]}"); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"- {a[x]}"); x++; }
            else { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"+ {b[y]}"); y++; }
        }
        while (x < n) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"- {a[x++]}"); }
        while (y < m) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"+ {b[y++]}"); }
    }
}