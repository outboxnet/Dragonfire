// Git/GitService.cs
using CodeAnalyzer.Core.Models;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Blob = LibGit2Sharp.Blob;

namespace CodeAnalyzer.Git;

public class GitService
{
    public async Task<List<AnalysisResult>> AnalyzeGitDiffAsync(string repoPath, string branch = null)
    {
        var results = new List<AnalysisResult>();

        using (var repo = new Repository(repoPath))
        {
            var changes = GetDiffChanges(repo, branch);

            foreach (var change in changes)
            {
                if (IsCSharpFile(change.Path))
                {
                    var tempFile = Path.GetTempFileName();
                    try
                    {
                        File.WriteAllText(tempFile, GetFileContent(repo, change));
                        var analyzer = new Analyzers.CodeAnalyzerEngine();
                        var result = await analyzer.AnalyzeFileAsync(tempFile);
                        result.FilePath = change.Path;
                        result.Issues = result.Issues.Where(i => IsInDiffRange(i.LineNumber, change)).ToList();
                        results.Add(result);
                    }
                    finally
                    {
                        File.Delete(tempFile);
                    }
                }
            }
        }

        return results;
    }

    public async Task<List<AnalysisResult>> AnalyzePullRequestAsync(
        string repoUrl,
        int prNumber,
        string githubToken = null)
    {
        var results = new List<AnalysisResult>();

        // Clone the repository temporarily
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var co = new CloneOptions();
            if (!string.IsNullOrEmpty(githubToken))
            {
                repoUrl = repoUrl.Replace("https://", $"https://{githubToken}@");
            }

            Repository.Clone(repoUrl, tempDir);

            using (var repo = new Repository(tempDir))
            {
                // Fetch PR branch
                var prRef = $"refs/pull/{prNumber}/head";
                var remote = repo.Network.Remotes["origin"];
                var refSpec = $"+{prRef}:{prRef}";
                Commands.Fetch(repo, remote.Name, new[] { refSpec }, new FetchOptions(), null);

                // Get PR changes
                var prBranch = repo.Branches[prRef];
                var targetBranch = repo.Branches["main"] ?? repo.Branches["master"];

                var changes = repo.Diff.Compare<TreeChanges>(targetBranch.Tip.Tree, prBranch.Tip.Tree);

                foreach (var change in changes)
                {
                    if (IsCSharpFile(change.Path))
                    {
                        var content = GetBlobContent(repo, change.Path, prBranch.Tip);
                        var tempFile = Path.GetTempFileName();
                        try
                        {
                            File.WriteAllText(tempFile, content);
                            var analyzer = new Analyzers.CodeAnalyzerEngine();
                            var result = await analyzer.AnalyzeFileAsync(tempFile);
                            result.FilePath = change.Path;
                            results.Add(result);
                        }
                        finally
                        {
                            File.Delete(tempFile);
                        }
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        return results;
    }

    private List<ChangeInfo> GetDiffChanges(Repository repo, string branch)
    {
        var changes = new List<ChangeInfo>();

        var targetBranch = string.IsNullOrEmpty(branch)
            ? repo.Head
            : repo.Branches[branch];

        var diff = repo.Diff.Compare<TreeChanges>(targetBranch.Tip.Tree, repo.Head.Tip.Tree);

        foreach (var change in diff)
        {
            var patch = repo.Diff.Compare<Patch>(targetBranch.Tip.Tree, repo.Head.Tip.Tree,
                new[] { change.Path });

            changes.Add(new ChangeInfo
            {
                Path = change.Path,
                Status = change.Status,
                Patch = patch,
                AddedLines = GetAddedLineNumbers(patch)
            });
        }

        return changes;
    }

    private bool IsCSharpFile(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private string GetFileContent(Repository repo, ChangeInfo change)
    {
        var targetBranch = repo.Head;
        var blob = targetBranch.Tip[change.Path]?.Target as LibGit2Sharp.Blob;
        return blob?.GetContentText() ?? "";
    }

    private string GetBlobContent(Repository repo, string path, Commit commit)
    {
        var blob = commit[path]?.Target as Blob;
        return blob?.GetContentText() ?? "";
    }

    private HashSet<int> GetAddedLineNumbers(Patch patch)
    {
        var lines = new HashSet<int>();
        if (patch == null) return lines;

        var content = patch.Content;
        var lines_content = content.Split('\n');
        int currentLine = 0;

        foreach (var line in lines_content)
        {
            if (line.StartsWith("@@"))
            {
                // Parse hunk header to get line numbers
                var match = System.Text.RegularExpressions.Regex.Match(line, @"\+(\d+)");
                if (match.Success)
                {
                    currentLine = int.Parse(match.Groups[1].Value);
                }
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                lines.Add(currentLine);
                currentLine++;
            }
            else if (!line.StartsWith("-"))
            {
                currentLine++;
            }
        }

        return lines;
    }

    private bool IsInDiffRange(int lineNumber, ChangeInfo change)
    {
        return change.AddedLines.Contains(lineNumber);
    }
}

public class ChangeInfo
{
    public string Path { get; set; }
    public ChangeKind Status { get; set; }
    public Patch Patch { get; set; }
    public HashSet<int> AddedLines { get; set; } = new();
}