using System;
using System.IO;
using Newtonsoft.Json.Schema;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Text;
using Newtonsoft.Json.Schema.Generation;

namespace csharp2jsonschema
{
    internal class Program
    {
        private static string _pathSource { get; set; } = "SchemaSrc";
        private static string _pathDest { get; set; } = "Schema";
        private static string _schemaDLL = "Schema.dll";
        private static string _commonNamespace = "Schema";
        private static void Main(string[] args)
        {
            var options = new NDesk.Options.OptionSet
            {
                { "src=", v=> _pathSource = v },
                { "dst=", v=> _pathDest = v },
            };
            Console.WriteLine("src: {0}. dst: {1}", _pathSource, _pathDest);
            options.Parse(args);
            var assembly = LoadAssembly(LoadSource(_pathSource));
            ClearDirectory(_pathDest);
            UpdateSchemas(assembly, _pathDest);
            Console.ReadKey();
        }

        private static string LoadSource(string path)
        {
            var files = new List<string>();
            DirSearch(ref files, path);
            var res = new StringBuilder();
            foreach (var file in files)
            {
                res.Append("namespace " + _commonNamespace + " {\n");
                res.Append(File.ReadAllText(file));
                res.Append("\n}");
                res.Append("\n");
            }
            return res.ToString();
        }

        private static Assembly LoadAssembly(string sourceCode)
        {
            //Console.WriteLine(sourceCode);
            var cp = new CompilerParameters();
            cp.GenerateExecutable = false;
            cp.GenerateInMemory = true;
            cp.OutputAssembly = _schemaDLL;

            var providerOptions = new Dictionary<string, string>();
            providerOptions.Add("CompilerVersion", "v4.0");
            CodeDomProvider compiler = CodeDomProvider.CreateProvider("C#", providerOptions);
            CompilerResults cr = compiler.CompileAssemblyFromSource(cp, sourceCode);
            if (cr.Errors.HasErrors)
            {
                StringBuilder errors = new StringBuilder("Compiler Errors :\r\n");
                foreach (CompilerError error in cr.Errors)
                {
                    errors.AppendFormat("Line {0},{1}\t: {2}\n", error.Line, error.Column, error.ErrorText);
                }
                Console.WriteLine(errors);
            }
            // verify assembly
            Assembly theDllAssembly = null;
            if (cp.GenerateInMemory)
                theDllAssembly = cr.CompiledAssembly;
            else
                theDllAssembly = Assembly.LoadFrom(_schemaDLL);

            //Type theClassType = theDllAssembly.GetType(sDynamClass);

            //foreach (Type type in theDllAssembly.GetTypes())
            //{
            //    if (type.IsClass == true)
            //    {
            //        if (type.FullName.EndsWith("." + sDynamClass))
            //        {
            //            theClassType = type;
            //            break;
            //        }
            //    }
            //}

            //// invoke the method
            //if (theClassType != null)
            //{
            //    object[] method_args = new object[] { };

            //    Object rslt = theClassType.InvokeMember(
            //        sDynamMethod,
            //      BindingFlags.Default | BindingFlags.InvokeMethod,
            //           null,
            //           null, // for static class
            //           method_args);

            //    Console.WriteLine("Results are: " + rslt.ToString());
            //}

            return theDllAssembly;
        }
        
        public static void UpdateSchemas(Assembly assembly, string dest)
        {
            var generator = new JSchemaGenerator();

            Dictionary<string, string> schemas = new Dictionary<string, string>();

            var q = from t in assembly.GetTypes()
                    where t.IsClass
                    select t;

            q.ToList().ForEach(t =>
            {
                if (t.Namespace == null)
                {
                    return;
                }
                string[] names = t.Namespace.Split('.');
                if (names.Length > 0 && names[0] == _commonNamespace)
                {
                    var schema = generator.Generate(t);
                    string path = dest;
                    foreach (var n in names)
                    {
                        path = Path.Combine(path, n);
                    }
                    Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, t.Name + ".json"), schema.ToString());
                    schemas[t.Namespace + "." + t.Name] = schema.ToString();
                    Console.WriteLine(t.Name + " in " + t.Namespace);
                }
            });
            

            List<string> list = new List<string>();
            DirSearch(ref list, Path.Combine(dest, _commonNamespace));
            string currentPath = "";
            string jsFile = "var JsonSchema = require('./../src/JsonSchema')\n";
            //jsFile += "var "+entryType.Namespace+" = {}\n";
            List<string> definedNamespaces = new List<string>();
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
                string[] currentPathSplitted = currentPath.Split(Path.DirectorySeparatorChar);
                currentPathSplitted = currentPathSplitted.Skip(2).ToArray();

                string nameSpace = System.String.Join(".", currentPathSplitted);
                if (!definedNamespaces.Contains(nameSpace))
                {
                    jsFile += nameSpace + " = {}\n";
                    definedNamespaces.Add(nameSpace);
                }
                string schemaName = nameSpace + "." + Path.GetFileNameWithoutExtension(e);
                jsFile += System.String.Format("{0} = new JsonSchema.SchemaEntity({1})\n",
                    schemaName, schemas[schemaName]);
            }
            jsFile += "module.exports = " + _commonNamespace;
            File.WriteAllText(Path.Combine(Path.Combine(dest, _commonNamespace), "schema.js"), jsFile);
        }

        static void DirSearch(ref List<string> list, string sDir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(sDir, "*.cs"))
                {
                    list.Add(f);
                }
                foreach (string d in Directory.GetDirectories(sDir))
                {
                    DirSearch(ref list, d);
                }
            }
            catch (System.Exception excpt)
            {
                Console.WriteLine(excpt.Message);
            }
        }
        static void ClearDirectory(string path)
        {
            DirectoryInfo di = new DirectoryInfo(path);

            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }
    }
}
