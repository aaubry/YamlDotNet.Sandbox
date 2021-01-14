﻿using Bullseye;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static build.Program;
using static SimpleExec.Command;
using Host = Bullseye.Host;

namespace build
{
    public static class BuildDefinition
    {
        public static GitVersion ResolveVersion(Options options, PreviousReleases releases)
        {
            var versionJson = Read("dotnet", $"gitversion /nofetch{(options.Verbose ? " /diag" : "")}", BasePath);
            WriteVerbose(versionJson);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            jsonOptions.Converters.Add(new AutoNumberToStringConverter());

            var version = JsonSerializer.Deserialize<GitVersion>(versionJson, jsonOptions);

            if (version.CommitsSinceVersionSource > 0 && version.Equals(releases.Latest))
            {
                ++version.Patch;
                WriteWarning("Patch was incremented because the version was not incremented since last release.");
            }

            if (version.IsPreRelease && NoPrerelease)
            {
                WriteWarning($"Forcing pre-release version '{version.PreReleaseLabel}' to be considered stable");
                version.PreReleaseLabel = null;
            }

            WriteImportant($"Current version is {version.NuGetVersion}");

            return version;
        }

        public static void SetBuildVersion(Options options, GitVersion version)
        {
            switch (options.Host)
            {
                case Host.Appveyor:
                    var buildNumber = Environment.GetEnvironmentVariable("APPVEYOR_BUILD_NUMBER");
                    Run("appveyor", $"UpdateBuild -Version {version.NuGetVersion}.{buildNumber}");
                    break;
            }
        }

        public static MetadataSet SetMetadata(GitVersion version)
        {
            var templatePath = Path.Combine(BasePath, "YamlDotNet", "Properties", "AssemblyInfo.template");
            WriteVerbose($"Using template {templatePath}");

            var template = File.ReadAllText(templatePath);
            var assemblyInfo = template
                .Replace("<%assemblyVersion%>", $"{version.Major}.0.0.0")
                .Replace("<%assemblyFileVersion%>", $"{version.MajorMinorPatch}.0")
                .Replace("<%assemblyInformationalVersion%>", version.NuGetVersion);

            var asssemblyInfoPath = Path.Combine(BasePath, "YamlDotNet", "Properties", "AssemblyInfo.cs");
            WriteVerbose($"Writing metadata to {asssemblyInfoPath}");
            File.WriteAllText(asssemblyInfoPath, assemblyInfo);

            return default;
        }

        public static SuccessfulBuild Build(Options options, MetadataSet _)
        {
            var verbosity = options.Verbose ? "detailed" : "minimal";
            Run("dotnet", $"build YamlDotNet.sln --configuration Release --verbosity {verbosity}", BasePath);

            return default;
        }

        public static SuccessfulUnitTests UnitTest(Options options, SuccessfulBuild _)
        {
            var verbosity = options.Verbose ? "detailed" : "minimal";
            Run("dotnet", $"test YamlDotNet.Test.csproj --no-build --configuration Release --verbosity {verbosity}", Path.Combine(BasePath, "YamlDotNet.Test"));

            return default;
        }

        public static SuccessfulAotTests AotTest(SuccessfulBuild _)
        {
            Run("wsl", $"--user root YamlDotNet.AotTest/run.sh", BasePath);

            return default;
        }

        public static NuGetPackage Pack(Options options, GitVersion version, SuccessfulUnitTests _, SuccessfulAotTests __)
        {
            var verbosity = options.Verbose ? "detailed" : "minimal";
            var buildDir = Path.Combine(BasePath, "YamlDotNet");
            Run("nuget", $"pack YamlDotNet.nuspec -Version {version.NuGetVersion} -OutputDirectory bin", buildDir);

            var packagePath = Path.Combine(buildDir, "bin", $"YamlDotNet.{version.NuGetVersion}.nupkg");
            return new NuGetPackage(packagePath);
        }

        public static void Publish(Options options, NuGetPackage package)
        {
            var apiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("NuGet API key is missing. Please set the NUGET_API_KEY environment variable.");
            }

            var isSandbox = options.Host switch
            {
                Host.Appveyor => Environment.GetEnvironmentVariable("APPVEYOR_REPO_NAME") != "aaubry/YamlDotNet",
                _ => false,
            };

            if (isSandbox)
            {
                WriteWarning("Skipped NuGet package publication because this is a sandbox environment");
            }
            else
            {
                Console.WriteLine($"nuget push {package.Path} -ApiKey *** -Source https://api.nuget.org/v3/index.json");
                Run("nuget", $"push {package.Path} -ApiKey {apiKey} -Source https://api.nuget.org/v3/index.json", noEcho: true);
            }
        }

        public static ScaffoldedRelease ScaffoldReleaseNotes(GitVersion version, PreviousReleases releases)
        {
            if (version.IsPreRelease)
            {
                throw new InvalidOperationException("Cannot release a pre-release version.");
            }

            var releaseNotesPath = Path.Combine(BasePath, "releases", $"{version.NuGetVersion}.md");

            string releaseNotes;
            bool reviewed;
            WriteVerbose($"ReleaseNotesPath: {releaseNotesPath}");
            if (File.Exists(releaseNotesPath))
            {
                WriteInformation("Keeping existing release notes.");

                releaseNotes = File.ReadAllText(releaseNotesPath);
                reviewed = true;
            }
            else
            {
                var previousVersion = releases.Versions.First();

                // Get the git log to scaffold the release notes
                string? currentHash = null;
                var commits = ReadLines("git", $"rev-list v{previousVersion}..HEAD --first-parent --reverse --pretty=tformat:%B")
                    .Select(l =>
                    {
                        var match = Regex.Match(l, "^commit (?<hash>[a-f0-9]+)$");
                        if (match.Success)
                        {
                            currentHash = match.Groups["hash"].Value;
                        }
                        return new
                        {
                            message = l,
                            commit = currentHash
                        };
                    })
                    .GroupBy(l => l.commit, (k, list) => new
                    {
                        commit = k,
                        message = list
                            .Skip(1)
                            .Select(l => Regex.Replace(l.message, @"\+semver:\s*\w+", "").Trim())
                            .Where(l => !string.IsNullOrEmpty(l))
                            .ToList()
                    });

                var log = commits
                    .Select(c => c.message.Select((l, i) => $"{(i == 0 ? '-' : ' ')} {l}"))
                    .Select(c => string.Join("  \n", c));

                releaseNotes = $"# Release {version.NuGetVersion}\n\n{string.Join("\n\n", log)}";

                File.WriteAllText(releaseNotesPath, releaseNotes);

                WriteImportant($"Please review the release notes:\n{releaseNotesPath}");
                reviewed = false;
            }
            WriteVerbose(releaseNotes);

            return new ScaffoldedRelease(releaseNotes, reviewed);
        }

        public static PreviousReleases DiscoverPreviousReleases()
        {
            // Find previous release
            var releases = ReadLines("git", "tag --list --merged master --format=\"%(refname:short)\" v*")
                .Select(tag => Regex.Match(tag.TrimEnd('\r'), @"^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$"))
                .Where(m => m.Success)
                .Select(match => new Version(
                    int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture)
                ))
                .OrderByDescending(v => v)
                .ToList();

            var previousReleases = new PreviousReleases(releases);
            WriteInformation($"The previous release was {previousReleases.Latest}");
            WriteVerbose("Releases:\n - " + string.Join("\n - ", releases));

            return previousReleases;
        }

        public static void Release(GitVersion version, ScaffoldedRelease scaffoldedRelease, PreviousReleases previousReleases)
        {
            if (!scaffoldedRelease.Reviewed)
            {
                WriteImportant("Please review the release notes before proceeding.");
                return;
            }

            var previousReleaseNotesLinks = previousReleases.Versions
                .Select(v => new
                {
                    Version = v,
                    RelativePath = $"releases/{v}.md",
                    AbsolutePath = Path.Combine(BasePath, "releases", $"{v}.md"),
                })
                .Where(r => File.Exists(r.AbsolutePath))
                .Select(r => $"- [{r.Version}]({r.RelativePath})");

            var releaseNotesFile = string.Join("\n",
                "# Release notes",
                "",
                Regex.Replace(scaffoldedRelease.ReleaseNotes, @"^#", "##"),
                "",
                "# Previous releases",
                "",
                string.Join("\n", previousReleaseNotesLinks)
            );

            var releaseNotesPath = Path.Combine(BasePath, "RELEASE_NOTES.md");
            File.WriteAllText(releaseNotesPath, releaseNotesFile);

            Run("git", $"add \"{releaseNotesPath}\"");
            Run("git", $"commit -m \"Prepare release {version.NuGetVersion}\"");
            Run("git", $"tag v{version.NuGetVersion}");

            WriteImportant($"Your release is ready. Remember to push it using the following commands:\n\n    git push && git push origin v{version.NuGetVersion}");
        }

        public static void Document(Options options)
        {
            var samplesProjectDir = Path.Combine(BasePath, "YamlDotNet.Samples");
            var samplesOutputDir = Path.Combine(BasePath, "..", "YamlDotNet.wiki");

            var verbosity = options.Verbose ? "detailed" : "minimal";
            Run("dotnet", $"test YamlDotNet.Samples.csproj --no-build --configuration Release --verbosity {verbosity} --logger \"trx;LogFileName=TestResults.trx\"", samplesProjectDir);

            var report = XDocument.Load(Path.Combine(samplesProjectDir, "TestResults", "TestResults.trx"));

            const string ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

            var testDefinitions = report.Root
                .Element(XName.Get("TestDefinitions", ns))
                .Elements(XName.Get("UnitTest", ns))
                .Select(e =>
                {
                    var testMethod = e.Element(XName.Get("TestMethod", ns));

                    var sampleClassName = testMethod.Attribute("className").Value;
                    var sampleMethodName = testMethod.Attribute("name").Value;

                    var testMethodAssembly = Assembly.LoadFrom(testMethod.Attribute("codeBase").Value);

                    var sampleClass = testMethodAssembly
                        .GetType(sampleClassName, true)!;

                    var sampleMethod = sampleClass
                        .GetMethod(sampleMethodName, BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidOperationException($"Method {sampleClassName}.{sampleMethodName} not found");

                    var sampleAttr = sampleMethod
                        .GetCustomAttributes()
                        .Single(a => a.GetType().Name == "SampleAttribute");

                    var description = UnIndent((string)sampleAttr.GetType().GetProperty("Description")!.GetValue(sampleAttr, null)!);

                    return new
                    {
                        Id = e.Attribute("id").Value,
                        Name = e.Attribute("name").Value,
                        Description = description,
                        Code = File.ReadAllText(Path.Combine(samplesProjectDir, $"{sampleClass.Name}.cs")),
                        FileName = $"Samples.{sampleClass.Name}.md",
                    };
                });

            var testResults = report.Root
                .Element(XName.Get("Results", ns))
                .Elements(XName.Get("UnitTestResult", ns))
                .Select(e => new
                {
                    TestId = e.Attribute("testId").Value,
                    Output = e
                        .Element(XName.Get("Output", ns))
                        ?.Element(XName.Get("StdOut", ns))
                        ?.Value
                });

            var samples = testDefinitions
                .GroupJoin(
                    testResults,
                    t => t.Id,
                    r => r.TestId,
                    (t, r) => new
                    {
                        t.Name,
                        t.Description,
                        t.Code,
                        t.FileName,
                        r.Single().Output, // For now we only know how to handle a single test result
                    }
                );

            var sampleList = new StringBuilder();

            foreach (var sample in samples)
            {
                WriteInformation($"Generating sample documentation page for {sample.Name}");

                File.WriteAllText(Path.Combine(samplesOutputDir, sample.FileName), @$"
# {sample.Name}

{sample.Description}

## Code

```C#
{sample.Code}
```

## Output

```
{sample.Output}
```
");

                sampleList
                    .AppendLine($"* *[{sample.Name}]({Path.GetFileNameWithoutExtension(sample.FileName)})*  ")
                    .AppendLine($"  {sample.Description.Replace("\n", "\n  ")}\n");
            }

            File.WriteAllText(Path.Combine(samplesOutputDir, "Samples.md"), $@"
# Samples

{sampleList}

* [Building Custom Formatters for .Net Core (Yaml Formatters)](http://www.fiyazhasan.me/building-custom-formatters-for-net-core-yaml-formatters/) by @FiyazBinHasan
");
        }
    }

    public class GitVersion : IEquatable<Version>
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
        public string? PreReleaseLabel { get; set; }

        public string NuGetVersion
        {
            get
            {
                return IsPreRelease
                    ? $"{MajorMinorPatch}-{PreReleaseLabel}"
                    : MajorMinorPatch;
            }
        }

        public string MajorMinorPatch => $"{Major}.{Minor}.{Patch}";

        public int CommitsSinceVersionSource { get; set; }

        public bool IsPreRelease => !string.IsNullOrEmpty(PreReleaseLabel);

        public bool Equals(Version? other)
        {
            return other is object
                && Major == other.Major
                && Minor == other.Minor
                && Patch == other.Build;
        }
    }

    public struct MetadataSet { }

    public struct SuccessfulBuild { }
    public struct SuccessfulAotTests { }
    public struct SuccessfulUnitTests { }

    public class ScaffoldedRelease
    {
        public ScaffoldedRelease(string releaseNotes, bool reviewed)
        {
            ReleaseNotes = releaseNotes;
            Reviewed = reviewed;
        }

        public string ReleaseNotes { get; set; }
        public bool Reviewed { get; }
    }

    public class PreviousReleases
    {
        public PreviousReleases(IEnumerable<Version> versions)
        {
            Versions = versions;
        }

        public IEnumerable<Version> Versions { get; }

        public Version Latest => Versions.First();
    }

    public class NuGetPackage
    {
        public NuGetPackage(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
