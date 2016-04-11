using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Text;
using Newtonsoft.Json.Schema.Generation;
using static System.String;

namespace csharp2jsonschema
{
    internal class Program
    {
        private static string PathSource { get; set; } = "SchemaSrc";
        private static string PathDest { get; set; } = "./";
        private static string _schemaDll = "";
        private static string CommonNamespace { get; set; } = "Common";
        private static string JClass { get; set; } = "./../src/JsonSchema";

        private static void Main(string[] args)
        {
            var options = new NDesk.Options.OptionSet
            {
                { "src=", v=> PathSource = v },
                { "dst=", v=> PathDest = v },
                { "n=", v=> CommonNamespace = v },
                { "jc=", v=> JClass = v },
            };
            options.Parse(args);
            _schemaDll = Path.Combine(PathDest, CommonNamespace, CommonNamespace + ".dll");
            var dest = Path.Combine(PathDest, CommonNamespace);
            ClearDirectory(dest);
            var assembly = LoadAssembly(LoadSource(PathSource));
            UpdateSchemas(assembly, PathDest);
            Console.WriteLine("Done");
        }

        private static string LoadSource(string path)
        {
            var files = new List<string>();
            DirSearch(ref files, path, "*.cs");
            var res = new StringBuilder();
            foreach (var file in files)
            {
                res.Append("namespace " + CommonNamespace + " {\n");
                res.Append(File.ReadAllText(file));
                res.Append("\n}");
                res.Append("\n");
            }
            return res.ToString();
        }

        private static Assembly LoadAssembly(string sourceCode)
        {
            //Console.WriteLine(sourceCode);
            var cp = new CompilerParameters
            {
                GenerateExecutable = false,
                GenerateInMemory = true,
                OutputAssembly = _schemaDll
            };

            var providerOptions = new Dictionary<string, string> { { "CompilerVersion", "v4.0" } };
            var compiler = CodeDomProvider.CreateProvider("C#", providerOptions);
            var cr = compiler.CompileAssemblyFromSource(cp, sourceCode);
            if (!cr.Errors.HasErrors) return cp.GenerateInMemory ? cr.CompiledAssembly : Assembly.LoadFrom(_schemaDll);
            var errors = new StringBuilder("Compiler Errors :\r\n");
            foreach (CompilerError error in cr.Errors)
            {
                errors.AppendFormat("Line {0},{1}\t: {2}\n", error.Line, error.Column, error.ErrorText);
            }
            Console.WriteLine(errors);
            return cp.GenerateInMemory ? cr.CompiledAssembly : Assembly.LoadFrom(_schemaDll);
        }

        public static void UpdateSchemas(Assembly assembly, string dest)
        {
            var generator = new JSchemaGenerator();
            var schemas = new Dictionary<string, string>();
            var q = from t in assembly.GetTypes()
                    where t.IsClass
                    select t;
            q.ToList().ForEach(t =>
            {
                if (t.Namespace == null)
                {
                    return;
                }
                var names = t.Namespace.Split('.');
                if (names.Length <= 0 || names[0] != CommonNamespace) return;
                var schema = generator.Generate(t);
                var path = names.Aggregate(dest, Path.Combine);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, t.Name + ".json"), schema.ToString());
                schemas[t.Namespace + "." + t.Name] = schema.ToString();
            });

            Console.WriteLine($"Schemas:");
            var list = new List<string>();
            DirSearch(ref list, Path.Combine(dest, CommonNamespace), "*.json");
            var jsFile = $"var Class = require('{JClass}')\n";
            var definedNamespaces = new List<string>();
            foreach (var e in list)
            {
                var currentPath = Path.GetDirectoryName(e);
                var nameSpace = Join(".", GetRelativePath(currentPath, "./").Split(Path.DirectorySeparatorChar));
                if (!definedNamespaces.Contains(nameSpace))
                {
                    jsFile += nameSpace + " = {}\n";
                    definedNamespaces.Add(nameSpace);
                }
                var schemaName = nameSpace + "." + Path.GetFileNameWithoutExtension(e);
                jsFile += $"{schemaName} = new Class({schemas[schemaName]})\n";
                Console.WriteLine($"    {schemaName}");
            }
            jsFile += "module.exports = " + CommonNamespace;
            File.WriteAllText(Path.Combine(Path.Combine(dest, CommonNamespace), CommonNamespace + ".js"), jsFile);
        }

        private static string GetRelativePath(string filespec, string folder)
        {
            var pathUri = new Uri(Path.GetFullPath(filespec));
            // Folders must end in a slash
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }
            var folderUri = new Uri(Path.GetFullPath(folder));
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private static void DirSearch(ref List<string> list, string sDir, string extension)
        {
            try
            {
                list.AddRange(Directory.GetFiles(sDir, extension));
                foreach (var d in Directory.GetDirectories(sDir))
                {
                    DirSearch(ref list, d, extension);
                }
            }
            catch (System.Exception excpt)
            {
                Console.WriteLine(excpt.Message);
            }
        }

        private static void ClearDirectory(string path)
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists)
            {
                Console.WriteLine($"Creating folder {path}");
                Directory.CreateDirectory(path);
                return;
            }
            else
            {
                Console.WriteLine($"Clearing folder {path}");
            }
            foreach (var file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (var dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }
    }
}
