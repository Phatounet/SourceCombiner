﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using System.Text.RegularExpressions;
using System.Reflection;

namespace SourceCombiner
{
    public sealed class SourceCombiner
    {
        private const string HelpCommands = "help -help --help -h  /? ?";
        private static readonly List<string> SourceFilesToIgnore = new List<string>
        {
            "AssemblyInfo.cs"
        };

        static void Main(string[] args)
        {
            if ((args.Length == 1 && HelpCommands.Contains(args[0])) || (args.Length < 2))
            {
                ShowHelp();
                return;
            }

            var solutionFilePath = args[0];
            var outputFilePath = args[1];
            var openFile = false;
            var minify = false;

            if (args.Length > 2)
            {
                bool.TryParse(args[2],out openFile);
            }

            if (args.Length > 3)
            {
                bool.TryParse(args[3], out minify);
            }


            var filesToParse = GetSourceFileNames(solutionFilePath);
            var namespaces = GetUniqueNamespaces(filesToParse);

            string outputSource = GenerateCombinedSource(namespaces,filesToParse);

            if (minify)
            {
                outputSource = StripComments(outputSource);
                outputSource = StripWhitespace(outputSource);
            }

            File.WriteAllText(outputFilePath,outputSource);

            if (openFile)
            {
                Process.Start(outputFilePath);
            }
        }

        private static string GenerateCombinedSource(List<string> namespaces,List<string> files)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(@"/*");
            sb.AppendLine($" * File generated by SourceCombiner.exe using {files.Count} source files.");
            sb.AppendLine($" * Created On: {DateTime.Now}");
            sb.AppendLine(@"*/");

            foreach (var ns in namespaces.OrderBy(s => s))
            {
                sb.AppendLine("using " + ns + ";");
            }

            foreach (var file in files)
            {
                IEnumerable<string> sourceLines = File.ReadAllLines(file);
                sb.AppendLine(@"//*** SourceCombiner -> original file " + Path.GetFileName(file) + " ***");
                var usingDirectiveStartsWith = "using ";
                foreach (var sourceLine in sourceLines)
                {
                    var trimmedLine = sourceLine.Trim().Replace("  ", " ");
                    var isUsingDirective = trimmedLine.StartsWith(usingDirectiveStartsWith) && trimmedLine.EndsWith(";");
                    if (!string.IsNullOrWhiteSpace(sourceLine) && !isUsingDirective)
                    {
                        sb.AppendLine(sourceLine);
                    }
                }
            }
    
            return sb.ToString();
        }

        private static List<string> GetSourceFileNames(string solutionFilePath)
        {
            List<string> files = new List<string>();
            SolutionFile solutionFile = SolutionFile.Parse(solutionFilePath);
            foreach (Project project in solutionFile.ProjectsInOrder.Select(p => new Project(p.AbsolutePath)))
            {
                foreach (ProjectItem item in project.AllEvaluatedItems.Where(item => item.ItemType == "Compile"))
                {
                    if (!SourceFilesToIgnore.Contains(Path.GetFileName(item.EvaluatedInclude)))
                    {
                        string projectFolder = Path.GetDirectoryName(project.FullPath);
                        string fullpath = Path.Combine(projectFolder, item.EvaluatedInclude);
                        files.Add(fullpath);
                    }
                }
            }

            return files;
        }


        private static List<string> GetUniqueNamespaces(List<string> files)
        {
            var names = new List<string>();
            const string openingTag = "using ";
            const int namespaceStartIndex = 6;

            foreach (var file in files)
            {
                IEnumerable<string> sourceLines = File.ReadAllLines(file);

                foreach (var sourceLine in sourceLines)
                {
                    var trimmedLine = sourceLine.Trim().Replace("  ", " ");
                    if (trimmedLine.StartsWith(openingTag) && trimmedLine.EndsWith(";"))
                    {
                        var name = trimmedLine.Substring(namespaceStartIndex, trimmedLine.Length - namespaceStartIndex - 1);

                        if (!names.Contains(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }

            return names;
        }

        /// <summary>Removes all comments from the source text</summary>
        /// <param name="source">The source file loaded into a string</param>
        /// <returns>A new string where all comments have been removed</returns>
        /// <remarks>https://stackoverflow.com/a/3524689/1363780</remarks>
        private static string StripComments(string source)
        {
            var cleanedSource = string.Empty;

            var blockComments = @"/\*(.*?)\*/";
            var lineComments = @"//(.*?)\r?\n";
            var strings = @"""((\\[^\n]|[^""\n])*)""";
            var verbatimStrings = @"@(""[^""]*"")+";

            var pattern = blockComments + "|" + lineComments + "|" + strings + "|" + verbatimStrings;
            var matchEvaluator = new MatchEvaluator(CommentEvaluator);

            cleanedSource = Regex.Replace(source, pattern ,matchEvaluator,RegexOptions.Singleline);

            return cleanedSource;
        }

        //MatchEvaluator Delegate: https://docs.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.matchevaluator?view=netframework-4.7.2
        private static string CommentEvaluator(Match match)
        {
            if (match.Value.StartsWith("/*") || match.Value.StartsWith("//"))
            {
                return string.Empty;// m.Value.StartsWith("//") ? Environment.NewLine : "";
            }
            return match.Value; // Keep the literal strings
        }


        private static string StripWhitespace(string source)
        {
            //Replace all the newlines
            var cleaned = source.Replace(Environment.NewLine, string.Empty);

            //It would be nice to also strip extra spaces and tabs
            return cleaned;
        }

        /// <summary>Shows a standard help info with information about the arguments</summary>
        private static void ShowHelp()
        {
            var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);

            Console.WriteLine($@"SourceCombiner.exe v{version.ProductMajorPart}.{version.ProductMinorPart}.{version.ProductPrivatePart}
Gerald Eckert @ 2019
https://github.com/GER-NaN/SourceCombiner

Combines multiple c# files and Visual Studio projects into a single 
consolidated source file. This is useful for submitting your work to a website
or online judge program that only accepts single files as input.

To combine multiple projects they must be added into the solution. A 
referenced project will be ignored.

Parameters
    -   Solution File (Required): The full file path to the solution (.sln) 
        file for your project.
    
    -   Output Location (Required): The full file path where the generated c#
        file should be output.

    -   Open When Done (Optional): A true or false value indicating whether 
        the generated file should be opened and displayed after generation. 
        The default value is false.

    -   Minify Output (Optional): A true or false value indicating whether the
        generated file should be minified. The minification process is not a 
        complete minification. Newlines and comments are the only items removed 
        from the source. The default value is false.");

        }
    }
}
