using CodeAnalyzer.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeAnalyzer.Analyzers;

public class CodeAnalyzerEngine
{
    public AnalyzerOptions Options { get; }

    public CodeAnalyzerEngine() : this(new AnalyzerOptions()) { }
    public CodeAnalyzerEngine(AnalyzerOptions options) { Options = options ?? new AnalyzerOptions(); }

    public async Task<AnalysisResult> AnalyzeFileAsync(string filePath)
    {
        var result = new AnalysisResult { FilePath = filePath };

        try
        {
            var code = await File.ReadAllTextAsync(filePath);

            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
            var root = await tree.GetRootAsync();

            var diagnostics = tree.GetDiagnostics();
            var hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

            if (hasErrors)
            {
                result.Issues.Add(new Issue
                {
                    Type = "ParseError",
                    Severity = "Error",
                    RuleName = "Syntax Error",
                    Message = "File contains syntax errors that prevent full analysis",
                    LineNumber = diagnostics.First(d => d.Severity == DiagnosticSeverity.Error)
                        .Location.GetLineSpan().StartLinePosition.Line + 1
                });
            }

            if (!hasErrors)
            {
                result.Issues.AddRange(AnalyzeSOLIDViolations(root));
                result.Issues.AddRange(AnalyzeNestingDepth(root));
                result.Issues.AddRange(AnalyzeNullReferences(root));
                result.Issues.AddRange(AnalyzeMethodParameters(root, filePath));
                result.Issues.AddRange(AnalyzeMethodLength(root, filePath));
                result.Issues.AddRange(AnalyzeMethodComplexity(root));
                result.Issues.AddRange(AnalyzeMagicNumbers(root, filePath));
                result.Issues.AddRange(AnalyzePublicMutableFields(root, filePath));
                result.Issues.AddRange(AnalyzeEmptyCatchBlocks(root, filePath));
                result.Issues.AddRange(AnalyzeTodoComments(root));
                result.Issues.AddRange(AnalyzeAsyncWithoutAwait(root));
                result.Issues.AddRange(AnalyzeUnusedPrivates(root));

                result.Complexity = CalculateComplexity(root);
            }
            else
            {
                result.Complexity = new ComplexityMetrics
                {
                    LineCount = code.Split('\n').Length,
                    ClassCount = 0,
                    MethodCount = 0,
                    CyclomaticComplexity = 0,
                    MaxNestingDepth = 0
                };
            }

            // Collect refactors from issues for easy lookup (deduped by Id).
            result.Refactors = result.Issues
                .Where(i => i.Suggestion != null)
                .Select(i => i.Suggestion)
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .ToList();

            result.OverallScore = CalculateOverallScore(result);
        }
        catch (Exception ex)
        {
            result.Issues.Add(new Issue
            {
                Type = "AnalysisError",
                Severity = "Error",
                RuleName = "Analysis Failed",
                Message = $"Failed to analyze file: {ex.Message}",
                LineNumber = 1
            });
            result.OverallScore = 0;
        }

        return result;
    }

    // ---------------------------------------------------------------------
    //  Existing rules (kept, but with refactor hooks where appropriate).
    // ---------------------------------------------------------------------

    private List<Issue> AnalyzeSOLIDViolations(SyntaxNode root)
    {
        var issues = new List<Issue>();

        try
        {
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classes)
            {
                var methods = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
                var properties = classDecl.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count();

                if (methods > 10 || (methods + properties) > 15)
                {
                    var className = classDecl.Identifier.Text;

                    // Issue 1 — SRP, with Extract Interface as the primary refactor.
                    var interfaceName = $"I{className}";
                    var hasPublicSurface = classDecl.Members.OfType<MethodDeclarationSyntax>()
                        .Any(m => m.Modifiers.Any(SyntaxKind.PublicKeyword) && !m.Modifiers.Any(SyntaxKind.StaticKeyword))
                        || classDecl.Members.OfType<PropertyDeclarationSyntax>()
                            .Any(p => p.Modifiers.Any(SyntaxKind.PublicKeyword) && !p.Modifiers.Any(SyntaxKind.StaticKeyword));

                    var srpIssue = new Issue
                    {
                        Type = "SOLID",
                        Severity = methods > 15 ? "Error" : "Warning",
                        RuleName = "Single Responsibility",
                        Message = $"Class '{className}' may violate SRP. Too many members ({methods} methods, {properties} properties). Consider extracting an interface to define its public contract.",
                        LineNumber = GetLineNumber(classDecl)
                    };
                    if (hasPublicSurface)
                    {
                        srpIssue.Suggestion = new RefactorSuggestion
                        {
                            Id = MakeId("ExtractInterface", className),
                            Kind = "ExtractInterface",
                            Title = $"Extract interface '{interfaceName}' from '{className}'",
                            Description = "Creates an interface from all public instance methods/properties and makes the class implement it.",
                            LineNumber = GetLineNumber(classDecl),
                            TargetSymbol = className,
                            Metadata =
                            {
                                ["class"] = className,
                                ["interfaceName"] = interfaceName
                            }
                        };
                    }
                    issues.Add(srpIssue);

                    // Issue 2 — only when the class hides a cluster of *private* helpers around a private field.
                    // That's the "hidden code that inflates complexity" pattern; safe to relocate without breaking callers.
                    var extractClassSuggestion = TryBuildExtractClassSuggestion(classDecl);
                    if (extractClassSuggestion != null)
                    {
                        var helperCount = extractClassSuggestion.Metadata.TryGetValue("methods", out var ms)
                            ? ms.Split('|', StringSplitOptions.RemoveEmptyEntries).Length
                            : 0;
                        var fieldName = extractClassSuggestion.Metadata.TryGetValue("field", out var f) ? f : "?";
                        var hiddenIssue = new Issue
                        {
                            Type = "SOLID",
                            Severity = "Warning",
                            RuleName = "Hidden Cluster",
                            Message = $"Class '{className}' contains {helperCount} private helper methods clustered around field '{fieldName}'. They inflate this class's cyclomatic complexity and could live on their own.",
                            LineNumber = GetLineNumber(classDecl),
                            Suggestion = extractClassSuggestion
                        };
                        issues.Add(hiddenIssue);
                    }
                }
            }

            var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();
            foreach (var interfaceDecl in interfaces)
            {
                var memberCount = interfaceDecl.Members.Count;
                if (memberCount > 5)
                {
                    issues.Add(new Issue
                    {
                        Type = "SOLID",
                        Severity = "Info",
                        RuleName = "Interface Segregation",
                        Message = $"Interface '{interfaceDecl.Identifier}' has {memberCount} members - consider splitting",
                        LineNumber = GetLineNumber(interfaceDecl)
                    });
                }
            }

            var fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (var field in fieldDeclarations)
            {
                var typeName = field.Declaration.Type.ToString();
                if (!IsSystemType(typeName) && !typeName.StartsWith("I") && !typeName.Contains("List") && !typeName.Contains("Dictionary"))
                {
                    issues.Add(new Issue
                    {
                        Type = "SOLID",
                        Severity = "Info",
                        RuleName = "Dependency Inversion",
                        Message = $"Concrete dependency '{typeName}' - consider using abstraction",
                        LineNumber = GetLineNumber(field)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add(new Issue
            {
                Type = "SOLID",
                Severity = "Warning",
                RuleName = "Analysis Error",
                Message = $"Error in SOLID analysis: {ex.Message}",
                LineNumber = 1
            });
        }

        return issues;
    }

    private List<Issue> AnalyzeNestingDepth(SyntaxNode root)
    {
        var issues = new List<Issue>();
        try
        {
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                if (method.Body == null) continue;
                var maxDepth = CalculateNestingDepth(method.Body);
                if (maxDepth > Options.MaxNestingDepth)
                {
                    issues.Add(new Issue
                    {
                        Type = "Nesting",
                        Severity = maxDepth > Options.MaxNestingDepth + 2 ? "Error" : "Warning",
                        RuleName = "Excessive Nesting",
                        Message = $"Method '{method.Identifier}' has nesting depth of {maxDepth}. Maximum recommended is {Options.MaxNestingDepth}",
                        LineNumber = GetLineNumber(method)
                    });
                }
            }
        }
        catch { }
        return issues;
    }

    private int CalculateNestingDepth(SyntaxNode node, int currentDepth = 0)
    {
        if (node == null) return currentDepth;
        var maxDepth = currentDepth;

        if (node is IfStatementSyntax || node is ForStatementSyntax || node is ForEachStatementSyntax ||
            node is WhileStatementSyntax || node is SwitchStatementSyntax || node is UsingStatementSyntax ||
            node is LockStatementSyntax || node is TryStatementSyntax)
        {
            currentDepth++;
            maxDepth = currentDepth;
        }

        foreach (var child in node.ChildNodes())
        {
            var childDepth = CalculateNestingDepth(child, currentDepth);
            maxDepth = Math.Max(maxDepth, childDepth);
        }
        return maxDepth;
    }

    private List<Issue> AnalyzeNullReferences(SyntaxNode root)
    {
        var issues = new List<Issue>();
        try
        {
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                foreach (var param in method.ParameterList.Parameters)
                {
                    var typeText = param.Type?.ToString() ?? "";
                    var isNullable = typeText.EndsWith("?");
                    var isValueType = IsLikelyValueType(typeText);
                    if (isNullable || isValueType) continue;

                    var hasNullCheck = method.DescendantNodes().OfType<BinaryExpressionSyntax>()
                        .Any(b => (b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression)) &&
                                  b.Left.ToString() == param.Identifier.Text &&
                                  b.Right.ToString() == "null");

                    hasNullCheck = hasNullCheck || method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(i => i.ToString().Contains($"ThrowIfNull({param.Identifier})"));

                    if (!hasNullCheck)
                    {
                        var refactorId = MakeId("AddNullCheck", method.Identifier.Text, param.Identifier.Text);
                        var issue = new Issue
                        {
                            Type = "NullReference",
                            Severity = "Warning",
                            RuleName = "Missing Null Check",
                            Message = $"Parameter '{param.Identifier}' in method '{method.Identifier}' could be null",
                            LineNumber = GetLineNumber(param),
                            Suggestion = new RefactorSuggestion
                            {
                                Id = refactorId,
                                Kind = "AddNullCheck",
                                Title = $"Add ArgumentNullException.ThrowIfNull({param.Identifier}) to '{method.Identifier}'",
                                Description = "Inserts a null guard at the top of the method body.",
                                LineNumber = GetLineNumber(method),
                                TargetSymbol = method.Identifier.Text,
                                Metadata =
                                {
                                    ["method"] = method.Identifier.Text,
                                    ["parameter"] = param.Identifier.Text
                                }
                            }
                        };
                        issues.Add(issue);
                    }
                }
            }
        }
        catch { }
        return issues;
    }

    // ---------------------------------------------------------------------
    //  New rules.
    // ---------------------------------------------------------------------

    /// <summary>Methods with too many parameters → suggest "Introduce Parameter Object".</summary>
    private List<Issue> AnalyzeMethodParameters(SyntaxNode root, string filePath)
    {
        var issues = new List<Issue>();
        var max = Options.MaxParameters;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var count = method.ParameterList.Parameters.Count;
            if (count <= max) continue;

            // Skip if it looks like a Main entry point.
            if (method.Identifier.Text == "Main") continue;

            var paramList = method.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier}")
                .ToList();

            var suggestedTypeName = $"{Capitalize(method.Identifier.Text)}Args";
            var refactorId = MakeId("IntroduceParameterObject", method.Identifier.Text);

            issues.Add(new Issue
            {
                Type = "Parameters",
                Severity = count > max + 3 ? "Error" : "Warning",
                RuleName = "Too Many Parameters",
                Message = $"Method '{method.Identifier}' has {count} parameters (max recommended: {max}). Consider grouping them into a parameter object.",
                LineNumber = GetLineNumber(method),
                Suggestion = new RefactorSuggestion
                {
                    Id = refactorId,
                    Kind = "IntroduceParameterObject",
                    Title = $"Introduce Parameter Object '{suggestedTypeName}' for '{method.Identifier}'",
                    Description = $"Replaces {count} parameters with a single '{suggestedTypeName}' record.",
                    LineNumber = GetLineNumber(method),
                    TargetSymbol = method.Identifier.Text,
                    Metadata =
                    {
                        ["method"] = method.Identifier.Text,
                        ["typeName"] = suggestedTypeName,
                        ["parameters"] = string.Join("|", paramList)
                    }
                }
            });
        }
        return issues;
    }

    /// <summary>Methods that exceed a line budget → suggest "Extract Method".</summary>
    private List<Issue> AnalyzeMethodLength(SyntaxNode root, string filePath)
    {
        var issues = new List<Issue>();
        var max = Options.MaxMethodLines;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body == null) continue;
            var span = method.Body.GetLocation().GetLineSpan();
            var lines = span.EndLinePosition.Line - span.StartLinePosition.Line;
            if (lines <= max) continue;

            var lengthIssue = new Issue
            {
                Type = "Length",
                Severity = lines > max * 2 ? "Error" : "Warning",
                RuleName = "Long Method",
                Message = $"Method '{method.Identifier}' is {lines} lines long (max recommended: {max}). Consider extracting cohesive blocks into helper methods.",
                LineNumber = GetLineNumber(method)
            };
            var extract = TryBuildExtractMethodSuggestion(method);
            if (extract != null) lengthIssue.Suggestions.Add(extract);
            issues.Add(lengthIssue);
        }
        return issues;
    }

    /// <summary>Methods with cyclomatic complexity above threshold → suggest "Extract Method".</summary>
    private List<Issue> AnalyzeMethodComplexity(SyntaxNode root)
    {
        var issues = new List<Issue>();
        var threshold = Options.MaxMethodComplexity;
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body == null) continue;
            var complexity = CalculateCyclomaticComplexity(method);
            if (complexity <= threshold) continue;

            var issue = new Issue
            {
                Type = "Complexity",
                Severity = complexity > threshold * 2 ? "Error" : "Warning",
                RuleName = "Method Too Complex",
                Message = $"Method '{method.Identifier}' has cyclomatic complexity {complexity} (max recommended: {threshold}). Extract a sub-block to lower it.",
                LineNumber = GetLineNumber(method)
            };
            var extract = TryBuildExtractMethodSuggestion(method);
            if (extract != null) issue.Suggestions.Add(extract);
            issues.Add(issue);
        }
        return issues;
    }

    /// <summary>Numeric literals that aren't 0/1/-1 sitting in expressions → suggest constant extraction.</summary>
    private List<Issue> AnalyzeMagicNumbers(SyntaxNode root, string filePath)
    {
        var issues = new List<Issue>();
        var allowed = new HashSet<string> { "0", "1", "-1", "2", "100" };

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.NumericLiteralExpression)) continue;
            var text = literal.Token.Text;
            if (allowed.Contains(text)) continue;

            // Skip literals inside enum/const declarations.
            if (literal.Ancestors().Any(a => a is EnumDeclarationSyntax || a is FieldDeclarationSyntax fd && fd.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword)))) continue;

            // Skip array sizes inside attribute arguments — usually fine.
            if (literal.Ancestors().Any(a => a is AttributeSyntax)) continue;

            issues.Add(new Issue
            {
                Type = "MagicNumber",
                Severity = "Info",
                RuleName = "Magic Number",
                Message = $"Magic number '{text}' — consider naming it as a const.",
                LineNumber = GetLineNumber(literal)
            });
        }
        return issues;
    }

    /// <summary>Public mutable fields → suggest converting to auto-property.</summary>
    private List<Issue> AnalyzePublicMutableFields(SyntaxNode root, string filePath)
    {
        var issues = new List<Issue>();
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var modifiers = field.Modifiers;
            if (!modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) continue;
            if (modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword) || m.IsKind(SyntaxKind.ReadOnlyKeyword))) continue;

            foreach (var v in field.Declaration.Variables)
            {
                var name = v.Identifier.Text;
                var refactorId = MakeId("EncapsulateField", name);
                var fieldIssue = new Issue
                {
                    Type = "Field",
                    Severity = "Warning",
                    RuleName = "Public Mutable Field",
                    Message = $"Public field '{name}' should be a property to preserve encapsulation.",
                    LineNumber = GetLineNumber(field),
                };
                fieldIssue.Suggestions.Add(new RefactorSuggestion
                {
                    Id = refactorId,
                    Kind = "EncapsulateField",
                    Title = $"Convert public field '{name}' to auto-property",
                    Description = "Replaces the field declaration with `public T Name { get; set; }`.",
                    LineNumber = GetLineNumber(field),
                    TargetSymbol = name,
                    Metadata =
                    {
                        ["field"] = name,
                        ["type"] = field.Declaration.Type.ToString()
                    }
                });
                issues.Add(fieldIssue);
            }
        }
        return issues;
    }

    /// <summary>Empty / swallowed catch blocks.</summary>
    private List<Issue> AnalyzeEmptyCatchBlocks(SyntaxNode root, string filePath)
    {
        var issues = new List<Issue>();
        // Group empty catches by their containing method so we can address them by ordinal.
        var perMethod = new Dictionary<string, int>();

        foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            var body = catchClause.Block;
            if (body == null || body.Statements.Any()) continue;

            var method = catchClause.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var methodName = method?.Identifier.Text ?? "<top>";
            if (!perMethod.ContainsKey(methodName)) perMethod[methodName] = 0;
            var ordinal = perMethod[methodName]++;

            var refactorId = MakeId("AddCatchHandler", methodName, ordinal.ToString());
            var catchIssue = new Issue
            {
                Type = "Catch",
                Severity = "Warning",
                RuleName = "Empty Catch Block",
                Message = "Catch block is empty — exceptions are silently swallowed.",
                LineNumber = GetLineNumber(catchClause),
            };
            catchIssue.Suggestions.Add(new RefactorSuggestion
            {
                Id = refactorId,
                Kind = "AddCatchHandler",
                Title = "Insert minimal logging into empty catch",
                Description = "Adds a Debug.WriteLine + TODO comment so exceptions stop being silenced.",
                LineNumber = GetLineNumber(catchClause),
                TargetSymbol = methodName,
                Metadata =
                {
                    ["method"] = methodName,
                    ["ordinal"] = ordinal.ToString()
                }
            });
            issues.Add(catchIssue);
        }
        return issues;
    }

    /// <summary>TODO / FIXME / HACK comments.</summary>
    private List<Issue> AnalyzeTodoComments(SyntaxNode root)
    {
        var issues = new List<Issue>();
        var trivias = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));

        foreach (var t in trivias)
        {
            var text = t.ToString();
            var upper = text.ToUpperInvariant();
            if (upper.Contains("TODO") || upper.Contains("FIXME") || upper.Contains("HACK"))
            {
                issues.Add(new Issue
                {
                    Type = "Todo",
                    Severity = "Info",
                    RuleName = "Open TODO",
                    Message = $"Pending: {text.Trim()}",
                    LineNumber = t.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }
        return issues;
    }

    /// <summary>async methods that never await anything.</summary>
    private List<Issue> AnalyzeAsyncWithoutAwait(SyntaxNode root)
    {
        var issues = new List<Issue>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) continue;
            if (method.Body == null && method.ExpressionBody == null) continue;

            var hasAwait = method.DescendantNodes().OfType<AwaitExpressionSyntax>().Any();
            if (!hasAwait)
            {
                issues.Add(new Issue
                {
                    Type = "Async",
                    Severity = "Warning",
                    RuleName = "Async Without Await",
                    Message = $"Method '{method.Identifier}' is marked async but never awaits — it will run synchronously.",
                    LineNumber = GetLineNumber(method)
                });
            }
        }
        return issues;
    }

    /// <summary>Private members that are declared but never referenced inside the file.</summary>
    private List<Issue> AnalyzeUnusedPrivates(SyntaxNode root)
    {
        var issues = new List<Issue>();
        var sourceText = root.ToFullString();

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            string name = null;
            int line = 0;

            if (member is MethodDeclarationSyntax m && m.Modifiers.Any(SyntaxKind.PrivateKeyword))
            {
                name = m.Identifier.Text;
                line = GetLineNumber(m);
            }
            else if (member is FieldDeclarationSyntax f && f.Modifiers.Any(SyntaxKind.PrivateKeyword))
            {
                name = f.Declaration.Variables.FirstOrDefault()?.Identifier.Text;
                line = GetLineNumber(f);
            }
            else continue;

            if (string.IsNullOrEmpty(name)) continue;
            // crude: count textual occurrences; >1 means it's used somewhere besides its own declaration.
            var count = System.Text.RegularExpressions.Regex.Matches(sourceText, $@"\b{System.Text.RegularExpressions.Regex.Escape(name)}\b").Count;
            if (count <= 1)
            {
                issues.Add(new Issue
                {
                    Type = "Unused",
                    Severity = "Info",
                    RuleName = "Unused Private Member",
                    Message = $"Private '{name}' appears unused in this file.",
                    LineNumber = line
                });
            }
        }
        return issues;
    }

    // ---------------------------------------------------------------------
    //  Metrics & helpers
    // ---------------------------------------------------------------------

    private ComplexityMetrics CalculateComplexity(SyntaxNode root)
    {
        var metrics = new ComplexityMetrics();
        try
        {
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            metrics.CyclomaticComplexity = methods.Sum(CalculateCyclomaticComplexity);
            metrics.MaxNestingDepth = methods.Any() ? methods.Max(m => m.Body != null ? CalculateNestingDepth(m.Body) : 0) : 0;
            metrics.LineCount = root.SyntaxTree.GetLineSpan(TextSpan.FromBounds(0, root.FullSpan.Length)).EndLinePosition.Line + 1;
            metrics.ClassCount = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
            metrics.MethodCount = methods.Count;
        }
        catch { }
        return metrics;
    }

    private int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        if (method.Body == null) return 1;
        int complexity = 1;
        try
        {
            complexity += method.Body.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<SwitchStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<DoStatementSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<CatchClauseSyntax>().Count();
            complexity += method.Body.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .Count(b => b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression));
        }
        catch { return 1; }
        return complexity;
    }

    private double CalculateOverallScore(AnalysisResult result)
    {
        double score = 100;
        try
        {
            score -= result.Issues.Count(i => i.Severity == "Error") * 10;
            score -= result.Issues.Count(i => i.Severity == "Warning") * 5;
            score -= result.Issues.Count(i => i.Severity == "Info") * 2;

            if (result.Complexity.CyclomaticComplexity > 50) score -= 20;
            else if (result.Complexity.CyclomaticComplexity > 30) score -= 10;
            else if (result.Complexity.CyclomaticComplexity > 15) score -= 5;

            if (result.Complexity.MaxNestingDepth > 5) score -= 15;
            else if (result.Complexity.MaxNestingDepth > 3) score -= 5;

            if (result.Issues.Any(i => i.Type == "ParseError")) score -= 30;
        }
        catch { return 0; }

        return Math.Max(0, Math.Min(100, score));
    }

    private int GetLineNumber(SyntaxNode node)
    {
        try { return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }
        catch { return 1; }
    }

    private bool IsSystemType(string typeName)
    {
        var systemTypes = new[] {
            "string", "int", "long", "decimal", "bool", "double", "float", "DateTime",
            "object", "byte", "char", "short", "uint", "ulong", "void"
        };
        return systemTypes.Contains(typeName) || typeName.StartsWith("System.") || typeName.StartsWith("Microsoft.");
    }

    private bool IsLikelyValueType(string typeName)
    {
        var valueTypes = new HashSet<string> {
            "int","long","short","byte","sbyte","uint","ulong","ushort",
            "bool","char","double","float","decimal","DateTime","Guid","TimeSpan"
        };
        return valueTypes.Contains(typeName);
    }

    private static string MakeId(string kind, params string[] parts)
    {
        var basis = kind + ":" + string.Join("/", parts);
        // FNV-1a 32-bit — deterministic across process restarts.
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in basis)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return $"{kind}-{hash:x8}";
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    // ---------------------------------------------------------------------
    //  Heuristic builders for "thinking" refactors (ExtractClass, ExtractMethod).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Looks for a cohesive cluster within the class: a private field that's used by a
    /// minority subset of methods. Returns a suggestion to extract that field + those
    /// methods into a new class. Returns null if no clean cluster can be found.
    /// </summary>
    private RefactorSuggestion TryBuildExtractClassSuggestion(ClassDeclarationSyntax classDecl)
    {
        var className = classDecl.Identifier.Text;
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count < 4) return null;

        // Private fields (explicit `private` or no modifier — implicit private).
        var privateFields = classDecl.Members.OfType<FieldDeclarationSyntax>()
            .Where(f => f.Modifiers.Any(SyntaxKind.PrivateKeyword) ||
                        !f.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword)))
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.Text))
            .ToList();
        if (privateFields.Count == 0) return null;

        // For each private field, the methods that mention it.
        (string field, List<MethodDeclarationSyntax> users) best = (null, null);
        foreach (var field in privateFields)
        {
            var users = methods.Where(m =>
                m.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Any(id => id.Identifier.Text == field)).ToList();

            // We want a "minority": >=2 methods but <= half. That's a real sub-responsibility.
            if (users.Count >= 2 && users.Count <= methods.Count / 2)
            {
                if (best.users == null || users.Count > best.users.Count)
                    best = (field, users);
            }
        }

        if (best.field == null) return null;

        var targetClassName = ToPascalCase(best.field) + "Service";
        var methodNames = best.users.Select(m => m.Identifier.Text).ToList();

        return new RefactorSuggestion
        {
            Id = MakeId("ExtractClass", className, best.field),
            Kind = "ExtractClass",
            Title = $"Extract '{targetClassName}' from '{className}' (around field '{best.field}')",
            Description = $"Moves field '{best.field}' and methods [{string.Join(", ", methodNames)}] to a new '{targetClassName}' class. Original class keeps a reference; review external call sites.",
            LineNumber = GetLineNumber(classDecl),
            TargetSymbol = className,
            Metadata =
            {
                ["sourceClass"] = className,
                ["targetClass"] = targetClassName,
                ["field"] = best.field,
                ["methods"] = string.Join("|", methodNames)
            }
        };
    }

    /// <summary>
    /// Picks the largest extractable inner block (≥3 statements) inside a method body.
    /// The refactor will move it into a new private helper. Captures method parameters
    /// and pre-block locals as helper parameters (best-effort, no semantic model).
    /// </summary>
    private RefactorSuggestion TryBuildExtractMethodSuggestion(MethodDeclarationSyntax method)
    {
        if (method.Body == null) return null;

        var inner = method.Body.DescendantNodes().OfType<BlockSyntax>()
            .Where(b => b != method.Body && b.Statements.Count >= 3)
            .ToList();
        if (inner.Count == 0) return null;

        // Pre-order index across all inner blocks (whether or not they qualify) so
        // analyzer + rewriter agree on the same anchor.
        var allInnerBlocks = method.Body.DescendantNodes().OfType<BlockSyntax>()
            .Where(b => b != method.Body)
            .ToList();
        var bestBlock = inner.OrderByDescending(b => b.Statements.Count).First();
        var ordinal = allInnerBlocks.IndexOf(bestBlock);

        var newName = $"{method.Identifier.Text}Step";
        return new RefactorSuggestion
        {
            Id = MakeId("ExtractMethod", method.Identifier.Text, ordinal.ToString()),
            Kind = "ExtractMethod",
            Title = $"Extract a {bestBlock.Statements.Count}-statement block of '{method.Identifier}' into '{newName}'",
            Description = "Moves the largest qualifying inner block into a private helper. Review parameter passing for any reassigned values.",
            LineNumber = bestBlock.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            TargetSymbol = method.Identifier.Text,
            Metadata =
            {
                ["method"] = method.Identifier.Text,
                ["ordinal"] = ordinal.ToString(),
                ["newName"] = newName
            }
        };
    }

    private static string ToPascalCase(string fieldName)
    {
        var trimmed = fieldName.TrimStart('_');
        if (string.IsNullOrEmpty(trimmed)) return "Extracted";
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
    }
}

/// <summary>Tunable thresholds for the analyzer.</summary>
public class AnalyzerOptions
{
    public int MaxParameters { get; set; } = 5;
    public int MaxMethodLines { get; set; } = 40;
    public int MaxNestingDepth { get; set; } = 3;
    public int MaxMethodComplexity { get; set; } = 10;
}
