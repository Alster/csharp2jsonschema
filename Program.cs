﻿using System;
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

        private static void Main(string[] args)
        {
            var options = new NDesk.Options.OptionSet
            {
                { "src=", v=> PathSource = v },
                { "dst=", v=> PathDest = v },
                { "n=", v=> CommonNamespace = v },
            };
            options.Parse(args);
            _schemaDll = Path.Combine(PathDest, CommonNamespace, CommonNamespace + ".dll");
            var dest = Path.Combine(PathDest, CommonNamespace);
            ClearDirectory(dest);
            var assembly = LoadAssembly(LoadSource(PathSource));
            UpdateSchemas(assembly, PathDest);
        }

        private static string LoadSource(string path)
        {
            var files = new List<string>();
            DirSearch(ref files, path);
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

            var providerOptions = new Dictionary<string, string> {{"CompilerVersion", "v4.0"}};
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
                Console.WriteLine(t.Name + " in " + t.Namespace);
            });
            

            var list = new List<string>();
            DirSearch(ref list, Path.Combine(dest, CommonNamespace));
            var currentPath = "";
            var jsFile = "var JsonSchema = require('./../src/JsonSchema')\n";
            //jsFile += "var "+entryType.Namespace+" = {}\n";
            var definedNamespaces = new List<string>();
            foreach (var e in list)
            {
                if (currentPath != Path.GetDirectoryName(e))
                {
                    currentPath = Path.GetDirectoryName(e);
                }
                if (Path.GetExtension(e) != ".json")
                {
                    continue;
                }
                var currentPathSplitted = currentPath.Split(Path.DirectorySeparatorChar);
                currentPathSplitted = currentPathSplitted.Skip(2).ToArray();

                var nameSpace = Join(".", currentPathSplitted);
                if (!definedNamespaces.Contains(nameSpace))
                {
                    jsFile += nameSpace + " = {}\n";
                    definedNamespaces.Add(nameSpace);
                }
                var schemaName = nameSpace + "." + Path.GetFileNameWithoutExtension(e);
                jsFile += $"{schemaName} = new JsonSchema.SchemaEntity({schemas[schemaName]})\n";
            }
            jsFile += "module.exports = " + CommonNamespace;
            File.WriteAllText(Path.Combine(Path.Combine(dest, CommonNamespace), CommonNamespace + ".js"), jsFile);
        }

        private static void DirSearch(ref List<string> list, string sDir)
        {
            try
            {
                list.AddRange(Directory.GetFiles(sDir, "*.cs"));
                foreach (var d in Directory.GetDirectories(sDir))
                {
                    DirSearch(ref list, d);
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
                Directory.CreateDirectory(path);
                return;
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
