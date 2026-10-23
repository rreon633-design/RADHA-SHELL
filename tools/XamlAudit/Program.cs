using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XamlAudit;

/// <summary>
/// Static verifier for the WinUI 3 layer. It cannot replace the Windows compiler, but it catches
/// the exact error classes that break a first WinUI build:
///
///   XA1  x:Class without a matching partial code-behind class
///   XA2  event handler referenced in XAML but missing from code-behind
///   XA3  {StaticResource} key that is never defined
///   XA4  {Binding} path whose first segment is not a property on any view model
///   XA5  x:Name used from code-behind but not declared in XAML
///   XA6  duplicate x:Name in one file
///   XA7  xmlns "using:" namespace that does not exist in source
///   XA8  custom element type that does not exist
///   XA9  ResourceDictionary Source pointing at a missing file
///   XA10 x:Bind (requires the Windows XAML compiler; forbidden by project policy)
///   XA11 XAML file with no code-behind
/// </summary>
public static class Program
{
    private sealed record Finding(string Code, string File, string Detail);

    private static readonly string[] EventAttributes =
    {
        "Click", "Tapped", "DoubleTapped", "RightTapped", "TextChanged", "KeyDown", "KeyUp",
        "ItemClick", "SelectionChanged", "Checked", "Unchecked", "Loaded", "Unloaded",
        "SizeChanged", "PointerPressed", "PointerReleased", "PointerEntered", "PointerExited",
        "DragOver", "Drop", "QuerySubmitted", "ValueChanged", "Expanding", "Collapsed",
        "ScrollViewer.ViewChanged"
    };

    private static readonly Regex ObservableProperty = new(
        @"\[ObservableProperty\][\s\S]{0,200}?\b_?([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|;|\{)", RegexOptions.Compiled);

    private static readonly Regex RelayCommandMethod = new(
        @"\[RelayCommand(?:\s*\([^\)]*\))?\][\s\S]{0,120}?\b(?:Task|void|async\s+Task)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex PublicProperty = new(
        @"public\s+(?:static\s+)?(?:readonly\s+)?[A-Za-z_<>,\.\?\[\] ]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)",
        RegexOptions.Compiled);

    /// <summary>public Type Member { … } — used to walk dotted binding paths.</summary>
    private static readonly Regex TypedProperty = new(
        @"public\s+(?:static\s+)?(?:readonly\s+)?([A-Za-z_][A-Za-z0-9_<>\.\?]*)>?\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)",
        RegexOptions.Compiled);

    /// <summary>[ObservableProperty] private Type _member;</summary>
    private static readonly Regex TypedObservable = new(
        @"\[ObservableProperty\][\s\S]{0,120}?(?:private|internal|protected|public)\s+(?:readonly\s+)?([A-Za-z_][A-Za-z0-9_<>\.\?]*)>?\s+_([A-Za-z_][A-Za-z0-9_]*)\s*(?:=|;)",
        RegexOptions.Compiled);

    private static readonly Regex NamespaceDecl = new(
        @"namespace\s+([A-Za-z0-9_.]+)\s*[;{]", RegexOptions.Compiled);

    private static readonly Regex ClassDecl = new(
        @"(?:public|internal|sealed|partial|abstract|\s)*class\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static readonly Regex FieldDecl = new(
        @"(?:private|internal|public|protected)\s+(?:static\s+)?(?:readonly\s+)?[A-Za-z_<>,\.\?\[\]]+\s+(_?[A-Za-z_][A-Za-z0-9_]*)\s*[;=]", RegexOptions.Compiled);

    private static readonly Regex LocalOrParam = new(
        @"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)|\(\s*[A-Za-z_][A-Za-z0-9_<>,\.\?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.Compiled);

    public static int Main(string[] args)
    {
        var root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindRepoRoot();
        var verbose = args.Contains("--verbose");

        if (root is null || !Directory.Exists(Path.Combine(root, "src", "Radha.App")))
        {
            Console.Error.WriteLine("XamlAudit: cannot locate src/Radha.App (pass the repo root as an argument)");
            return 2;
        }

        var appDir = Path.Combine(root, "src", "Radha.App");
        var files = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        var csFiles = Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToArray();

        // ---- index the source ----
        var properties = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var classes = new HashSet<string>(StringComparer.Ordinal);
        var codeBehind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // class → (member → declared type)
        var typeOfMember = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in ObservableProperty.Matches(text))
                properties.Add(ToPascal(m.Groups[1].Value));
            foreach (Match m in PublicProperty.Matches(text))
                properties.Add(m.Groups[1].Value);
            // CommunityToolkit.Mvvm source-generates <Method>Command for every [RelayCommand],
            // dropping a trailing "Async" (RefreshWallpaperAsync → RefreshWallpaperCommand).
            foreach (Match m in RelayCommandMethod.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5)
                    name = name[..^5];
                properties.Add(name + "Command");
            }
            foreach (Match m in NamespaceDecl.Matches(text))
                namespaces.Add(m.Groups[1].Value);
            var declaredClasses = ClassDecl.Matches(text).Select(m => m.Groups[1].Value).ToList();
            foreach (var name in declaredClasses) classes.Add(name);

            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in TypedProperty.Matches(text))
            {
                var type = m.Groups[1].Value.TrimEnd('?');
                var name = m.Groups[2].Value;
                if (name is "get" or "set" or "init" or "value") continue;
                members[name] = StripGeneric(type);
            }
            foreach (Match m in TypedObservable.Matches(text))
            {
                var type = m.Groups[1].Value.TrimEnd('?');
                var field = m.Groups[2].Value;
                members[char.ToUpperInvariant(field[0]) + field[1..]] = StripGeneric(type);
            }
            // generated command properties belong to the class too (type is a toolkit command)
            foreach (Match m in RelayCommandMethod.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5) name = name[..^5];
                members.TryAdd(name + "Command", "IRelayCommand");
            }

            foreach (var declaredClass in declaredClasses)
                typeOfMember[declaredClass] = members;
            if (file.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
                codeBehind[Path.ChangeExtension(file, null)!.Replace(".xaml", ".xaml", StringComparison.Ordinal)] = text;
        }

        // lines that are keywords/types, not members we can resolve statically
        var knownKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "this", "base", "value", "sender", "e", "args", "true", "false", "null", "var", "new",
            "string", "int", "double", "bool", "float", "byte", "object", "char", "Task", "void",
            "Console", "Math", "Path", "File", "Directory", "Environment", "Guid", "DateTime",
            "TimeSpan", "Enumerable", "Array", "String", "List", "Dictionary", "Process", "Marshal",
            "Color", "Colors", "JsonSerializer", "App", "Ui", "NativeMethods", "WindowNative",
            "Win32Interop", "DisplayArea", "AppWindow", "RectInt32", "WindowId", "DispatcherTimer",
            "Thread", "CultureInfo", "Uri", "Type", "Activator", "Debug", "Trace", "Regex"
        };

        var findings = new List<Finding>();
        var resourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var xamlNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var parsed = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

        // pass 1: parse + collect resource keys and names
        foreach (var file in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(file, LoadOptions.SetLineInfo); }
            catch (Exception ex)
            {
                findings.Add(new Finding("XA0", file, $"not well-formed XML: {ex.Message}"));
                continue;
            }
            parsed[file] = doc;

            foreach (var element in doc.Descendants())
            {
                var key = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;
                if (key is not null) resourceKeys.Add(key);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in doc.Descendants().Attributes())
            {
                if (!attribute.Name.LocalName.Equals("Name", StringComparison.Ordinal)) continue;
                if (attribute.Name.NamespaceName.Contains("winfx/2006/xaml")) names.Add(attribute.Value);
            }
            xamlNames[file] = names;
        }

        // pass 2: verify
        foreach (var (file, doc) in parsed.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var source = File.ReadAllText(file);

            // XA10 — x:Bind needs the Windows compiler, project policy forbids it
            if (source.Contains("x:Bind", StringComparison.Ordinal))
                findings.Add(new Finding("XA10", relative, "x:Bind is not allowed; use {Binding} with item-level brushes"));

            var rootElement = doc.Root!;
            var className = rootElement.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "Class" && a.Name.NamespaceName.Contains("winfx/2006/xaml"))?.Value;

            // XA11 / XA1 — code-behind presence and class match
            if (className is not null)
            {
                var cbPath = file + ".cs";
                if (!File.Exists(cbPath))
                {
                    findings.Add(new Finding("XA11", relative, $"missing code-behind {Path.GetFileName(cbPath)}"));
                }
                else if (!codeBehind.TryGetValue(file, out var cbText) || !cbText.Contains($"class {className.Split('.').Last()}"))
                {
                    findings.Add(new Finding("XA1", relative, $"x:Class '{className}' has no matching partial class in code-behind"));
                }
            }

            // XA6 — duplicate x:Name
            var duplicateNames = doc.Descendants().Attributes()
                .Where(a => a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("winfx/2006/xaml"))
                .GroupBy(a => a.Value)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var duplicate in duplicateNames)
                findings.Add(new Finding("XA6", relative, $"duplicate x:Name '{duplicate}'"));

            // XA2 — event handlers must exist in the code-behind
            var cbTextAll = File.Exists(file + ".cs") ? File.ReadAllText(file + ".cs") : "";
            foreach (var attribute in doc.Descendants().Attributes())
            {
                var name = attribute.Name.LocalName;
                if (!EventAttributes.Contains(name)) continue;
                var handler = attribute.Value.Trim();
                if (handler.Length == 0 || handler.Contains('{') || handler.Contains("Binding")) continue;
                var method = handler.Split('(', ',')[0].Trim();
                if (!Regex.IsMatch(cbTextAll, $@"\b(void|Task|async\s+void)\s+{Regex.Escape(method)}\s*\(") &&
                    !Regex.IsMatch(cbTextAll, $@"\b{Regex.Escape(method)}\s*\("))
                {
                    findings.Add(new Finding("XA2", relative, $"{name}=\"{handler}\" has no handler '{method}' in code-behind"));
                }
            }

            // XA3 — StaticResource keys
            foreach (Match match in Regex.Matches(source, @"\{StaticResource\s+([A-Za-z0-9_.]+)\s*\}"))
            {
                var key = match.Groups[1].Value;
                if (!resourceKeys.Contains(key))
                    findings.Add(new Finding("XA3", relative, $"StaticResource '{key}' is never defined"));
            }

            // XA4 — binding first segment must be a known property
            foreach (Match match in Regex.Matches(source, @"\{Binding\s+([^\}]+)\}"))
            {
                var expression = match.Groups[1].Value;
                if (expression.Contains("ElementName=") || expression.Contains("Source=") ||
                    expression.Contains("RelativeSource=")) continue;

                var path = expression.Split(',')[0].Trim();
                if (path is "" or ".") continue;
                var first = path.Split('.')[0];
                if (first.Contains('=')) continue;

                if (!properties.Contains(first) && !knownKeywords.Contains(first))
                {
                    findings.Add(new Finding("XA4", relative, $"{{Binding {expression}}} — '{first}' is not a property on any view model"));
                    continue;
                }

                // XA12 — walk the rest of the path through declared member types
                var segments = path.Split('.');
                if (segments.Length > 1)
                {
                    var candidates = typeOfMember
                        .Where(kv => kv.Value.ContainsKey(segments[0]))
                        .Select(kv => kv.Value[segments[0]])
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    for (var i = 1; i < segments.Length && candidates.Count > 0; i++)
                    {
                        var segment = segments[i];
                        var next = new List<string>();
                        var resolvedAny = false;

                        foreach (var type in candidates)
                        {
                            if (!typeOfMember.TryGetValue(type, out var lookup)) continue;
                            resolvedAny = true;
                            if (lookup.TryGetValue(segment, out var nextType)) next.Add(nextType);
                        }

                        if (!resolvedAny) break;                        // type lives outside the app
                        if (next.Count == 0)
                        {
                            findings.Add(new Finding("XA12", relative,
                                $"{{Binding {expression}}} — '{segment}' does not exist on {string.Join(" / ", candidates)}"));
                            break;
                        }
                        candidates = next.Distinct(StringComparer.Ordinal).ToList();
                    }
                }
            }

            // XA7 / XA8 — custom namespaces and element types
            var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in rootElement.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                var value = attribute.Value;
                if (value.StartsWith("using:", StringComparison.Ordinal))
                    prefixes[attribute.Name.LocalName] = value["using:".Length..];
            }

            foreach (var (prefix, ns) in prefixes)
            {
                if (!namespaces.Contains(ns))
                {
                    findings.Add(new Finding("XA7", relative, $"xmlns:{prefix}=\"using:{ns}\" — namespace not found in source"));
                    continue;
                }

                foreach (var element in doc.Descendants().Where(e => e.Name.NamespaceName == ns))
                {
                    var typeName = element.Name.LocalName;
                    if (!classes.Contains(typeName))
                        findings.Add(new Finding("XA8", relative, $"<{prefix}:{typeName}> — class '{typeName}' not found in source"));
                }
            }

            // XA9 — ResourceDictionary sources
            foreach (Match match in Regex.Matches(source, @"Source=""ms-appx:///([^""]+)"""))
            {
                var target = Path.Combine(appDir, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(target))
                    findings.Add(new Finding("XA9", relative, $"ResourceDictionary source '{match.Groups[1].Value}' does not exist"));
            }

            // XA5 — identifiers from code-behind that look like element access
            if (File.Exists(file + ".cs"))
            {
                var cb = File.ReadAllText(file + ".cs");
                var declared = xamlNames[file];
                var localNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in FieldDecl.Matches(cb))
                    localNames.Add(m.Groups[1].Value);
                foreach (Match m in LocalOrParam.Matches(cb))
                {
                    if (m.Groups[1].Success) localNames.Add(m.Groups[1].Value);
                    if (m.Groups[2].Success) localNames.Add(m.Groups[2].Value);
                }

                foreach (Match m in Regex.Matches(cb, @"(?<![.\w])([A-Z][A-Za-z0-9_]*)\.(Children|Text|Focus|DataContext|ItemsSource|Visibility|ActualWidth|ActualHeight|SelectedItem|SelectedIndex|IsChecked|Tag)\b"))
                {
                    var identifier = m.Groups[1].Value;
                    // skip members of a longer qualified name (e.g. Microsoft.UI.Text…)
                    if (declared.Contains(identifier) || localNames.Contains(identifier) ||
                        classes.Contains(identifier) || knownKeywords.Contains(identifier)) continue;
                    findings.Add(new Finding("XA5", relative, $"code-behind uses '{identifier}.{m.Groups[2].Value}' but no x:Name '{identifier}' exists in this file"));
                }
            }
        }

        // ---- report ----
        Console.WriteLine($"XamlAudit · {files.Length} xaml files · {csFiles.Length} cs files · " +
                          $"{properties.Count} members indexed · {resourceKeys.Count} resource keys");
        Console.WriteLine();

        if (findings.Count == 0)
        {
            Console.WriteLine("no findings — every x:Class, handler, resource key, binding root, namespace and element resolves");
            return 0;
        }

        foreach (var group in findings.GroupBy(f => f.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{group.Key} ({group.Count()})");
            foreach (var finding in group)
                Console.WriteLine($"  {finding.File}: {finding.Detail}");
            Console.WriteLine();
        }

        Console.WriteLine($"{findings.Count} finding(s)");
        return 1;

        static string StripGeneric(string type)
        {
            var angled = type.IndexOf('<');
            return angled > 0 ? type[..angled] : type;
        }

        static string ToPascal(string field)
        {
            var trimmed = field.TrimStart('_');
            return trimmed.Length == 0 ? field : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        }

        static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src", "Radha.App"))) return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
