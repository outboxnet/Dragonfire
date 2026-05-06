using CodeAnalyzer.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeAnalyzer.Analyzers;

/// <summary>
/// Applies automated refactors discovered by <see cref="CodeAnalyzerEngine"/>.
///
/// Refactors can declare dependencies on other refactors (in the same file). Apply
/// resolves those into a topological order before performing edits, so you can ask
/// for a single refactor and get all the supporting changes for free with --with-deps.
/// </summary>
public class RefactorEngine
{
    private readonly CodeAnalyzerEngine _analyzer;

    public RefactorEngine() : this(new CodeAnalyzerEngine()) { }
    public RefactorEngine(CodeAnalyzerEngine analyzer) { _analyzer = analyzer; }

    public async Task<List<RefactorSuggestion>> ListAsync(string filePath)
    {
        var result = await _analyzer.AnalyzeFileAsync(filePath);
        return result.Refactors;
    }

    public class ApplyResult
    {
        public bool Changed { get; set; }
        public string OriginalText { get; set; }
        public string NewText { get; set; }
        public List<string> AppliedRefactorIds { get; } = new();
        public List<string> SkippedReasons { get; } = new();
    }

    /// <summary>
    /// Apply one or more refactor IDs to a file. If withDeps is true, every refactor's
    /// declared dependencies are pulled in (transitively) and ordered before it.
    /// Pass writeToDisk=true to persist; otherwise the result is returned for preview.
    /// </summary>
    public async Task<ApplyResult> ApplyAsync(string filePath, IEnumerable<string> refactorIds, bool withDeps, bool writeToDisk)
    {
        var result = new ApplyResult();
        var analysis = await _analyzer.AnalyzeFileAsync(filePath);
        var byId = analysis.Refactors.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

        var requested = refactorIds.ToList();
        var ordered = ResolveOrder(requested, byId, withDeps, result.SkippedReasons);
        if (ordered.Count == 0)
        {
            result.OriginalText = await File.ReadAllTextAsync(filePath);
            result.NewText = result.OriginalText;
            return result;
        }

        var source = await File.ReadAllTextAsync(filePath);
        result.OriginalText = source;

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = await tree.GetRootAsync();

        foreach (var refactor in ordered)
        {
            try
            {
                root = ApplyOne(root, refactor);
                result.AppliedRefactorIds.Add(refactor.Id);
            }
            catch (Exception ex)
            {
                result.SkippedReasons.Add($"{refactor.Id}: {ex.Message}");
            }
        }

        var newText = root.NormalizeWhitespace().ToFullString();
        result.NewText = newText;
        result.Changed = !string.Equals(source, newText, StringComparison.Ordinal);

        if (writeToDisk && result.Changed)
        {
            await File.WriteAllTextAsync(filePath, newText);
        }

        return result;
    }

    // -------------------------------------------------------------------
    //  Dependency resolution
    // -------------------------------------------------------------------

    private List<RefactorSuggestion> ResolveOrder(
        List<string> requestedIds,
        Dictionary<string, RefactorSuggestion> byId,
        bool withDeps,
        List<string> skippedReasons)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<RefactorSuggestion>();

        void Visit(string id, Stack<string> path)
        {
            if (visited.Contains(id)) return;
            if (path.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                skippedReasons.Add($"{id}: cyclic dependency, skipped");
                return;
            }
            if (!byId.TryGetValue(id, out var refactor))
            {
                skippedReasons.Add($"{id}: not found in current analysis");
                return;
            }

            path.Push(id);
            if (withDeps)
            {
                foreach (var dep in refactor.Dependencies)
                    Visit(dep, path);
            }
            path.Pop();

            visited.Add(id);
            ordered.Add(refactor);
        }

        foreach (var id in requestedIds)
            Visit(id, new Stack<string>());

        return ordered;
    }

    // -------------------------------------------------------------------
    //  Refactor implementations
    // -------------------------------------------------------------------

    private SyntaxNode ApplyOne(SyntaxNode root, RefactorSuggestion r) => r.Kind switch
    {
        "AddNullCheck"            => ApplyAddNullCheck(root, r),
        "EncapsulateField"        => ApplyEncapsulateField(root, r),
        "IntroduceParameterObject"=> ApplyIntroduceParameterObject(root, r),
        "AddCatchHandler"         => ApplyAddCatchHandler(root, r),
        "ExtractInterface"        => ApplyExtractInterface(root, r),
        "ExtractClass"            => ApplyExtractClass(root, r),
        "ExtractMethod"           => ApplyExtractMethod(root, r),
        _ => throw new NotSupportedException($"Refactor kind '{r.Kind}' is not implemented.")
    };

    private SyntaxNode ApplyAddNullCheck(SyntaxNode root, RefactorSuggestion r)
    {
        var methodName = r.Metadata["method"];
        var paramName = r.Metadata["parameter"];

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName &&
                                 m.ParameterList.Parameters.Any(p => p.Identifier.Text == paramName));

        if (method?.Body == null) return root;

        // Idempotent: don't insert if a guard already exists.
        var alreadyGuarded = method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(i => i.ToString().Contains($"ThrowIfNull({paramName})"));
        if (alreadyGuarded) return root;

        var guard = SyntaxFactory.ParseStatement(
            $"System.ArgumentNullException.ThrowIfNull({paramName});\n");

        var newBody = method.Body.WithStatements(
            method.Body.Statements.Insert(0, guard));

        return root.ReplaceNode(method.Body, newBody);
    }

    private SyntaxNode ApplyEncapsulateField(SyntaxNode root, RefactorSuggestion r)
    {
        var fieldName = r.Metadata["field"];
        var typeText = r.Metadata["type"];

        // Find the field declaration that contains the variable with this name.
        var field = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == fieldName));

        if (field == null) return root;

        // Build: public T Name { get; set; } = initializer?;
        var variable = field.Declaration.Variables.First(v => v.Identifier.Text == fieldName);
        var initializer = variable.Initializer; // may be null

        var prop = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(typeText),
                SyntaxFactory.Identifier(fieldName))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            })));

        if (initializer != null)
        {
            prop = prop.WithInitializer(initializer)
                       .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }

        // If the field declared multiple variables, only remove this variable; otherwise replace whole field.
        if (field.Declaration.Variables.Count == 1)
        {
            return root.ReplaceNode(field, prop);
        }
        else
        {
            var remainingVars = field.Declaration.Variables.Where(v => v.Identifier.Text != fieldName);
            var trimmedField = field.WithDeclaration(
                field.Declaration.WithVariables(SyntaxFactory.SeparatedList(remainingVars)));
            // Insert the new property right after the trimmed field.
            var parent = field.Parent;
            if (parent is TypeDeclarationSyntax type)
            {
                var idx = type.Members.IndexOf(field);
                var newMembers = type.Members.Replace(field, trimmedField).Insert(idx + 1, prop);
                return root.ReplaceNode(type, type.WithMembers(newMembers));
            }
            return root.ReplaceNode(field, trimmedField);
        }
    }

    /// <summary>
    /// Replaces a method's parameter list with a single record parameter and inserts
    /// the record declaration into the same containing type. The method body is rewritten
    /// to read parameters via the new object (e.g. `name` → `args.name`).
    /// </summary>
    private SyntaxNode ApplyIntroduceParameterObject(SyntaxNode root, RefactorSuggestion r)
    {
        var methodName = r.Metadata["method"];
        var typeName = r.Metadata["typeName"];
        var paramSpecs = r.Metadata["parameters"].Split('|', StringSplitOptions.RemoveEmptyEntries);

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);
        if (method == null) return root;

        var containingType = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType == null) return root;

        // Build: public record TypeName(T1 p1, T2 p2, ...);
        var recordParams = paramSpecs.Select(spec =>
        {
            var idx = spec.LastIndexOf(' ');
            var t = spec.Substring(0, idx).Trim();
            var n = spec.Substring(idx + 1).Trim();
            return SyntaxFactory.Parameter(SyntaxFactory.Identifier(n))
                .WithType(SyntaxFactory.ParseTypeName(t));
        });
        var recordDecl = SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), typeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(recordParams)))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        // Build new parameter list: (TypeName args).
        var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("args"))
            .WithType(SyntaxFactory.ParseTypeName(typeName));
        var newParamList = SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(newParam));

        // Rewrite identifier references inside the method body: paramName → args.ParamName.
        var paramNames = paramSpecs.Select(spec => spec.Substring(spec.LastIndexOf(' ') + 1).Trim()).ToHashSet();
        SyntaxNode newMethodBodyNode = method.Body;
        BlockSyntax rewrittenBody = method.Body;
        if (rewrittenBody != null)
        {
            rewrittenBody = (BlockSyntax)new ParameterRewriter(paramNames, "args").Visit(rewrittenBody);
        }

        var newMethod = method.WithParameterList(newParamList);
        if (rewrittenBody != null) newMethod = newMethod.WithBody(rewrittenBody);

        // Replace the method first, then insert the record declaration above it in the same type.
        var newRoot = root.ReplaceNode(method, newMethod);
        var typeAfter = newRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .First(t => t.Identifier.Text == containingType.Identifier.Text);

        var methodAfter = typeAfter.Members.OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        // Don't double-insert if a same-named record already exists.
        var alreadyHas = typeAfter.Members.OfType<RecordDeclarationSyntax>()
            .Any(rec => rec.Identifier.Text == typeName)
            || newRoot.DescendantNodes().OfType<RecordDeclarationSyntax>()
                .Any(rec => rec.Identifier.Text == typeName);

        if (alreadyHas) return newRoot;

        var idx = typeAfter.Members.IndexOf(methodAfter);
        var newMembers = typeAfter.Members.Insert(idx, recordDecl);
        return newRoot.ReplaceNode(typeAfter, typeAfter.WithMembers(newMembers));
    }

    private SyntaxNode ApplyAddCatchHandler(SyntaxNode root, RefactorSuggestion r)
    {
        var methodName = r.Metadata["method"];
        var ordinal = int.Parse(r.Metadata["ordinal"]);

        IEnumerable<CatchClauseSyntax> empties;
        if (methodName == "<top>")
        {
            empties = root.DescendantNodes().OfType<CatchClauseSyntax>()
                .Where(c => c.Block != null && !c.Block.Statements.Any() &&
                            !c.Ancestors().OfType<MethodDeclarationSyntax>().Any());
        }
        else
        {
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == methodName);
            if (method == null) return root;
            empties = method.DescendantNodes().OfType<CatchClauseSyntax>()
                .Where(c => c.Block != null && !c.Block.Statements.Any());
        }

        var catchClause = empties.ElementAtOrDefault(ordinal);
        if (catchClause?.Block == null) return root;

        // If there's no exception variable, add one so we can reference it.
        var declaration = catchClause.Declaration;
        var exVarName = declaration?.Identifier.Text;
        CatchDeclarationSyntax newDecl = declaration;
        if (declaration == null)
        {
            exVarName = "ex";
            newDecl = SyntaxFactory.CatchDeclaration(SyntaxFactory.ParseTypeName("System.Exception"))
                .WithIdentifier(SyntaxFactory.Identifier("ex"));
        }
        else if (string.IsNullOrEmpty(exVarName))
        {
            exVarName = "ex";
            newDecl = declaration.WithIdentifier(SyntaxFactory.Identifier("ex"));
        }

        var stmt = SyntaxFactory.ParseStatement(
            $"System.Diagnostics.Debug.WriteLine(\"[swallowed exception] \" + {exVarName}); // TODO: handle exception\n");

        var newBlock = catchClause.Block.WithStatements(SyntaxFactory.SingletonList(stmt));
        var newCatch = catchClause.WithBlock(newBlock);
        if (newDecl != declaration) newCatch = newCatch.WithDeclaration(newDecl);

        return root.ReplaceNode(catchClause, newCatch);
    }

    // -------------------------------------------------------------------
    //  ExtractInterface — produces I<ClassName> from public surface.
    // -------------------------------------------------------------------

    private SyntaxNode ApplyExtractInterface(SyntaxNode root, RefactorSuggestion r)
    {
        var className = r.Metadata["class"];
        var ifaceName = r.Metadata["interfaceName"];

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);
        if (classDecl == null) return root;

        // Idempotent: skip if interface already exists in this compilation unit.
        if (root.DescendantNodes().OfType<InterfaceDeclarationSyntax>()
                .Any(i => i.Identifier.Text == ifaceName))
            return root;

        // Method signatures.
        var methodSigs = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword) && !m.Modifiers.Any(SyntaxKind.StaticKeyword))
            .Select(m => SyntaxFactory.MethodDeclaration(m.ReturnType, m.Identifier)
                .WithParameterList(m.ParameterList.WithoutTrivia())
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
            .Cast<MemberDeclarationSyntax>()
            .ToList();

        // Property signatures (preserve get/set shape).
        var propSigs = classDecl.Members.OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(SyntaxKind.PublicKeyword) && !p.Modifiers.Any(SyntaxKind.StaticKeyword))
            .Select(p =>
            {
                var accessors = p.AccessorList?.Accessors
                    .Select(a => SyntaxFactory.AccessorDeclaration(a.Kind())
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                    .ToArray()
                    ?? new[] { SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                 .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)) };
                return SyntaxFactory.PropertyDeclaration(p.Type, p.Identifier)
                    .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));
            })
            .Cast<MemberDeclarationSyntax>()
            .ToList();

        if (methodSigs.Count == 0 && propSigs.Count == 0) return root;

        var iface = SyntaxFactory.InterfaceDeclaration(ifaceName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List(methodSigs.Concat(propSigs)));

        // Add `: IClassName` to the class's base list (or create one).
        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(ifaceName));
        ClassDeclarationSyntax newClass;
        if (classDecl.BaseList == null)
        {
            newClass = classDecl.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        }
        else if (classDecl.BaseList.Types.Any(t => t.Type.ToString() == ifaceName))
        {
            newClass = classDecl;
        }
        else
        {
            newClass = classDecl.WithBaseList(classDecl.BaseList.AddTypes(baseType));
        }

        var rootWithClass = root.ReplaceNode(classDecl, newClass);

        // Insert interface before class in the same parent container.
        var classAfter = rootWithClass.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == className);

        return InsertSiblingBefore(rootWithClass, classAfter, iface);
    }

    // -------------------------------------------------------------------
    //  ExtractClass — moves a cohesive cluster of members to a new class.
    // -------------------------------------------------------------------

    private SyntaxNode ApplyExtractClass(SyntaxNode root, RefactorSuggestion r)
    {
        var sourceClassName = r.Metadata["sourceClass"];
        var targetClassName = r.Metadata["targetClass"];
        var fieldName = r.Metadata["field"];
        var methodNames = r.Metadata["methods"]
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        var sourceClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == sourceClassName);
        if (sourceClass == null) return root;

        // Idempotent: already extracted.
        if (root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == targetClassName))
            return root;

        var methodsToMove = sourceClass.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => methodNames.Contains(m.Identifier.Text))
            .ToList();
        if (methodsToMove.Count == 0) return root;

        var fieldToMove = sourceClass.Members.OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.Text == fieldName));

        // Build the new class.
        var newClassMembers = new List<MemberDeclarationSyntax>();
        if (fieldToMove != null)
        {
            // Drop the `private` modifier on the moved field if it had one — make it private inside the new class.
            newClassMembers.Add(fieldToMove);
        }
        newClassMembers.AddRange(methodsToMove);

        var newClass = SyntaxFactory.ClassDeclaration(targetClassName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(SyntaxFactory.List(newClassMembers));

        // Build the residual source class: drop moved members, add a backing field for the new instance.
        var remaining = sourceClass.Members.Where(m =>
        {
            if (m is MethodDeclarationSyntax md && methodNames.Contains(md.Identifier.Text)) return false;
            if (fieldToMove != null && m == fieldToMove) return false;
            return true;
        }).ToList();

        var instanceFieldName = "_" + char.ToLowerInvariant(targetClassName[0]) + targetClassName.Substring(1);
        var instanceField = (MemberDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
            $"private readonly {targetClassName} {instanceFieldName} = new();");
        if (instanceField != null) remaining.Insert(0, instanceField);

        var todoComment = SyntaxFactory.Comment(
            $"// TODO: review external call sites — methods {string.Join(", ", methodNames)} were moved into '{targetClassName}'.\n");

        var updatedSource = sourceClass
            .WithMembers(SyntaxFactory.List(remaining))
            .WithLeadingTrivia(sourceClass.GetLeadingTrivia().Add(todoComment));

        var rootAfterSource = root.ReplaceNode(sourceClass, updatedSource);

        // Drop the new class as a sibling right after the source.
        var sourceAfter = rootAfterSource.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == sourceClassName);

        return InsertSiblingAfter(rootAfterSource, sourceAfter, newClass);
    }

    // -------------------------------------------------------------------
    //  ExtractMethod — moves an inner block into a new private helper.
    // -------------------------------------------------------------------

    private SyntaxNode ApplyExtractMethod(SyntaxNode root, RefactorSuggestion r)
    {
        var methodName = r.Metadata["method"];
        var ordinal = int.Parse(r.Metadata["ordinal"]);
        var newName = r.Metadata["newName"];

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);
        if (method?.Body == null) return root;

        var allInnerBlocks = method.Body.DescendantNodes().OfType<BlockSyntax>()
            .Where(b => b != method.Body).ToList();
        if (ordinal < 0 || ordinal >= allInnerBlocks.Count) return root;
        var targetBlock = allInnerBlocks[ordinal];
        if (targetBlock.Statements.Count < 3) return root;

        // Identify candidate parameter names available at the extraction site:
        //  - all method parameters
        //  - locals declared earlier in the method body
        var paramTypes = method.ParameterList.Parameters
            .ToDictionary(p => p.Identifier.Text, p => p.Type?.ToString() ?? "object");

        var blockStart = targetBlock.SpanStart;
        var preLocals = new Dictionary<string, string>();
        foreach (var local in method.Body.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (local.SpanStart >= blockStart) continue;
            // Skip if local is *inside* targetBlock (not a real "before" candidate).
            if (targetBlock.Span.Contains(local.Span)) continue;
            var declaredType = local.Declaration.Type?.ToString() ?? "var";
            foreach (var v in local.Declaration.Variables)
                preLocals[v.Identifier.Text] = declaredType;
        }

        // Identifier references inside the block.
        var refs = targetBlock.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(i => i.Identifier.Text)
            .Distinct()
            .ToList();

        var paramSpecs = new List<(string type, string name)>();
        foreach (var name in refs)
        {
            if (paramTypes.TryGetValue(name, out var t)) paramSpecs.Add((t, name));
            else if (preLocals.TryGetValue(name, out var lt))
            {
                if (lt == "var") lt = "object"; // lossy without semantic model — flagged by TODO comment below
                paramSpecs.Add((lt, name));
            }
        }

        var helperParamList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
            paramSpecs.Select(p => SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.name))
                .WithType(SyntaxFactory.ParseTypeName(p.type)))));

        // The helper body is a copy of the block's statements (don't reuse the same node — Roslyn syntax is immutable but parents must be unique).
        var helperBody = SyntaxFactory.Block(targetBlock.Statements);

        // Optional TODO trivia.
        var hasVarLocal = paramSpecs.Any(p => preLocals.ContainsKey(p.name) && preLocals[p.name] == "var");
        var todoTrivia = SyntaxFactory.TriviaList(
            SyntaxFactory.Comment("// TODO: review parameter passing — extracted from " + methodName +
                                   (hasVarLocal ? " (some `var` types defaulted to `object`)" : "") + "\n"));

        var helper = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                newName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithParameterList(helperParamList)
            .WithBody(helperBody)
            .WithLeadingTrivia(todoTrivia);

        // Replace the block's contents with a single call to the helper.
        var argList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            paramSpecs.Select(p => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(p.name)))));
        var callStmt = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(newName), argList));
        var newBlock = targetBlock.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(callStmt));

        var rootAfterBlock = root.ReplaceNode(targetBlock, newBlock);

        // Insert the helper just after the original method in the containing type.
        var methodAfter = rootAfterBlock.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);
        var typeAfter = methodAfter.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeAfter == null) return rootAfterBlock;

        var idx = typeAfter.Members.IndexOf(methodAfter);
        var newMembers = typeAfter.Members.Insert(idx + 1, helper);
        return rootAfterBlock.ReplaceNode(typeAfter, typeAfter.WithMembers(newMembers));
    }

    // -------------------------------------------------------------------
    //  Sibling-insertion helpers (works inside namespace, compilation unit, or outer type).
    // -------------------------------------------------------------------

    private static SyntaxNode InsertSiblingBefore(SyntaxNode root, MemberDeclarationSyntax anchor, MemberDeclarationSyntax toInsert)
        => InsertSibling(root, anchor, toInsert, before: true);

    private static SyntaxNode InsertSiblingAfter(SyntaxNode root, MemberDeclarationSyntax anchor, MemberDeclarationSyntax toInsert)
        => InsertSibling(root, anchor, toInsert, before: false);

    private static SyntaxNode InsertSibling(SyntaxNode root, MemberDeclarationSyntax anchor, MemberDeclarationSyntax toInsert, bool before)
    {
        var parent = anchor.Parent;
        switch (parent)
        {
            case BaseNamespaceDeclarationSyntax ns:
                {
                    var idx = ns.Members.IndexOf(anchor);
                    var newMembers = ns.Members.Insert(before ? idx : idx + 1, toInsert);
                    return root.ReplaceNode(ns, ns.WithMembers(newMembers));
                }
            case CompilationUnitSyntax cu:
                {
                    var idx = cu.Members.IndexOf(anchor);
                    var newMembers = cu.Members.Insert(before ? idx : idx + 1, toInsert);
                    return root.ReplaceNode(cu, cu.WithMembers(newMembers));
                }
            case TypeDeclarationSyntax outer:
                {
                    var idx = outer.Members.IndexOf(anchor);
                    var newMembers = outer.Members.Insert(before ? idx : idx + 1, toInsert);
                    return root.ReplaceNode(outer, outer.WithMembers(newMembers));
                }
            default:
                return root;
        }
    }

    /// <summary>Rewrites bare identifier references for a known set of names into member accesses on a target.</summary>
    private sealed class ParameterRewriter : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _names;
        private readonly string _target;
        public ParameterRewriter(HashSet<string> names, string target) { _names = names; _target = target; }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!_names.Contains(node.Identifier.Text)) return base.VisitIdentifierName(node);

            // Skip if part of a member access where this identifier is the right-hand side: `foo.x`
            if (node.Parent is MemberAccessExpressionSyntax mae && mae.Name == node)
                return base.VisitIdentifierName(node);

            // Skip if the parent is a name colon (named argument), parameter declaration, etc.
            if (node.Parent is NameColonSyntax || node.Parent is ParameterSyntax) return node;

            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(_target),
                node);
        }
    }
}
