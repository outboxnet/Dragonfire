// Models/AnalysisResult.cs
using System.Collections.Generic;

namespace CodeAnalyzer.Core.Models;

public class AnalysisResult
{
    public string FilePath { get; set; }
    public List<Issue> Issues { get; set; } = new();
    public ComplexityMetrics Complexity { get; set; }
    public double OverallScore { get; set; }

    /// <summary>All refactors discovered in this file (also reachable via Issue.Suggestion).</summary>
    public List<RefactorSuggestion> Refactors { get; set; } = new();
}

public class Issue
{
    public string Type { get; set; } // SOLID, Nesting, NullRef, Complexity, Parameters, Length, MagicNumber, Field, Catch, Todo, Unused
    public string Severity { get; set; } // Error, Warning, Info
    public string Message { get; set; }
    public int LineNumber { get; set; }
    public string CodeSnippet { get; set; }
    public string RuleName { get; set; }

    /// <summary>Optional automated fix proposal for this issue.</summary>
    public RefactorSuggestion Suggestion { get; set; }
}

public class ComplexityMetrics
{
    public int CyclomaticComplexity { get; set; }
    public int MaxNestingDepth { get; set; }
    public int LineCount { get; set; }
    public int ClassCount { get; set; }
    public int MethodCount { get; set; }
}

/// <summary>
/// A specific automated refactor that can be applied via the CLI.
/// The Id is stable for a given (file, kind, target) tuple within an analysis run.
/// </summary>
public class RefactorSuggestion
{
    public string Id { get; set; }
    public string Kind { get; set; } // IntroduceParameterObject, AddNullCheck, EncapsulateField, ExtractConstant, AddCatchHandler, ConvertSyncToAsync
    public string Title { get; set; }
    public string Description { get; set; }
    public int LineNumber { get; set; }

    /// <summary>The symbol the refactor targets — method name, field name, etc.</summary>
    public string TargetSymbol { get; set; }

    /// <summary>Free-form payload (e.g. parameter list to bundle, default value, type name).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>Other refactor Ids that must be applied first (or alongside) — e.g. create a Record before updating the method signature.</summary>
    public List<string> Dependencies { get; set; } = new();
}
