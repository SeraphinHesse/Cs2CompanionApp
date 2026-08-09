using System;
using System.Collections.Generic;
using System.Diagnostics;
using Agora.Mod.Llm;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The command line the Claude CLI is actually invoked with.
    ///
    /// <para>
    /// These are not tautologies restating the concatenation. The runner has two branches, and only
    /// one of them is exercised on a developer machine whose PATH reaches a <c>claude.exe</c>. The
    /// other — the npm <c>claude.cmd</c> shim, which <c>ClaudeCliLocator.ExecutableNames</c> ranks
    /// first because it is what <c>npm i -g</c> installs and what <c>CreateProcess</c> refuses to
    /// run — nests the whole argument tail inside one outer pair of quotes. <c>cmd /s /c</c> strips
    /// the first and last quote of that string and passes the rest through verbatim, so an argument
    /// that quotes its own value compiles, reads as careful, and silently changes what runs. That is
    /// a bug with no symptom for its author and a symptom for most players.
    /// </para>
    /// </summary>
    public class ClaudeCliArgumentTests
    {
        private const string CmdShim = @"C:\Users\dev\AppData\Roaming\npm\claude.cmd";
        private const string Exe = @"C:\Users\dev\AppData\Local\Programs\claude\claude.exe";

        [Fact]
        public void DefaultModel_IsTheAliasNotADatedSnapshot()
        {
            // A pin retires; an alias does not. When a pin 404s the flavor call fails closed
            // (non-negotiable #7) into canned prose, in every save, with nothing but a Debug line to
            // say so — which is exactly the kind of failure nobody notices for a season.
            Assert.Equal("claude-haiku-4-5", ClaudeCliOptions.DefaultModel);
            Assert.Equal("claude-haiku-4-5", new ClaudeCliOptions().Model);
            Assert.DoesNotContain("-2025", ClaudeCliOptions.DefaultModel);
        }

        [Fact]
        public void DirectBranch_PassesTheArgumentsWithNoShellWrapper()
        {
            ProcessStartInfo info = ClaudeCliRunner.BuildStartInfo(Exe, new ClaudeCliOptions());

            Assert.Equal(Exe, info.FileName);
            Assert.Equal("-p --output-format json --model claude-haiku-4-5", info.Arguments);
            Assert.DoesNotContain("\"", info.Arguments);
        }

        [Fact]
        public void CmdBranch_ProducesExactlyOneOuterQuotePairAroundTheTail()
        {
            ProcessStartInfo info = ClaudeCliRunner.BuildStartInfo(CmdShim, new ClaudeCliOptions());

            Assert.Equal("cmd.exe", info.FileName);
            Assert.Equal(
                "/d /s /c \"\"" + CmdShim + "\" -p --output-format json --model claude-haiku-4-5\"",
                info.Arguments);

            // Spelled out as well as pinned, because the equality above would still pass if someone
            // "fixed" both the expectation and the code in the same wrong direction.
            const string prefix = "/d /s /c ";
            string tail = info.Arguments.Substring(prefix.Length);
            Assert.StartsWith(prefix, info.Arguments);
            Assert.StartsWith("\"", tail);
            Assert.EndsWith("\"", tail);

            // cmd /s strips the first and last quote and re-parses nothing else. What is left must
            // therefore be a quoted program path followed by bare arguments: four quotes in total,
            // the outer pair and the pair around the path. A fifth would mean the model argument
            // brought quotes of its own and the pairing has moved.
            Assert.Equal(4, CountQuotes(info.Arguments));
            Assert.Contains("\"" + CmdShim + "\" ", info.Arguments);
            Assert.Contains(" --model claude-haiku-4-5", info.Arguments);
            Assert.DoesNotContain("--model \"", info.Arguments);
        }

        [Fact]
        public void BothBranches_CarryTheSameModelArgumentText()
        {
            // The regression that matters: two hand-assembled strings drift when someone edits one.
            var options = new ClaudeCliOptions { Model = "claude-sonnet-4-5" };

            ProcessStartInfo direct = ClaudeCliRunner.BuildStartInfo(Exe, options);
            ProcessStartInfo shell = ClaudeCliRunner.BuildStartInfo(CmdShim, options);

            string shared = ClaudeCliRunner.BuildArguments(options);
            Assert.Equal("-p --output-format json --model claude-sonnet-4-5", shared);
            Assert.Equal(shared, direct.Arguments);
            Assert.Contains(shared, shell.Arguments);
        }

        [Theory]
        [InlineData("haiku\" & calc")]      // the quote that breaks cmd's outer pair
        [InlineData("haiku&calc")]          // command separator
        [InlineData("haiku|calc")]          // pipe
        [InlineData("haiku^x")]             // cmd's escape character
        [InlineData("haiku>out.txt")]       // redirection
        [InlineData("%APPDATA%")]           // variable expansion, done by cmd before the shim sees it
        [InlineData("claude haiku 4 5")]    // a space is two arguments, not one
        [InlineData("--dangerously-skip-permissions")] // a value posing as a second flag
        [InlineData("-verbose")]            // any leading dash, not just the double one
        [InlineData("claude-haiku-4-5-ä")]  // non-ASCII: outside the whitelist by construction
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void RejectedModelId_FallsBackToTheDefaultAndNeverReachesTheCommandLine(string? candidate)
        {
            Assert.False(ClaudeCliOptions.IsValidModelId(candidate!));

            // Set through the property, which is the one door a model id can come through.
            var options = new ClaudeCliOptions();
            options.Model = candidate!;
            Assert.Equal(ClaudeCliOptions.DefaultModel, options.Model);

            string direct = ClaudeCliRunner.BuildStartInfo(Exe, options).Arguments;
            string shell = ClaudeCliRunner.BuildStartInfo(CmdShim, options).Arguments;

            Assert.EndsWith("--model " + ClaudeCliOptions.DefaultModel, direct);
            Assert.Equal(4, CountQuotes(shell));
            if (!string.IsNullOrEmpty(candidate) && candidate.Trim().Length > 0)
            {
                Assert.DoesNotContain(candidate.Trim(), direct);
                Assert.DoesNotContain(candidate.Trim(), shell);
            }
        }

        [Fact]
        public void NullOptions_BuildsTheDefaultCommandLineRatherThanThrowing()
        {
            // BuildArguments has always tolerated null; BuildStartInfo used to dereference
            // WorkingDirectory and NRE. Run coalesces before it calls, so this was unreachable — but
            // nothing on the flavor path may throw (non-negotiable #7), and a pair of neighbouring
            // methods that disagree about null is a trap laid for the next caller.
            ProcessStartInfo shell = ClaudeCliRunner.BuildStartInfo(CmdShim, null);
            ProcessStartInfo direct = ClaudeCliRunner.BuildStartInfo(Exe, null);

            Assert.Equal(
                "/d /s /c \"\"" + CmdShim + "\" -p --output-format json --model claude-haiku-4-5\"",
                shell.Arguments);
            Assert.Equal("-p --output-format json --model claude-haiku-4-5", direct.Arguments);
        }

        [Fact]
        public void ModelIdLongerThanTheBound_IsRejected()
        {
            string tooLong = new string('a', ClaudeCliOptions.MaxModelIdLength + 1);

            Assert.False(ClaudeCliOptions.IsValidModelId(tooLong));
            Assert.True(ClaudeCliOptions.IsValidModelId(tooLong.Substring(1)));
        }

        [Fact]
        public void EnvironmentOverride_IsHonouredWhenValid()
        {
            // Through the injected reader, the same seam ClaudeCliLocator uses — mutating the real
            // process environment would leak into whatever test ran next.
            ClaudeCliOptions options = ClaudeCliOptions.FromEnvironment(
                null, Env(ClaudeCliOptions.ModelEnvVar, " claude-opus-4-1 "));

            Assert.Equal("claude-opus-4-1", options.Model);
            Assert.Equal(
                "-p --output-format json --model claude-opus-4-1",
                ClaudeCliRunner.BuildArguments(options));
        }

        [Fact]
        public void EnvironmentOverride_ThatIsNotAModelId_LeavesTheDefaultAndLogs()
        {
            var log = new RecordingLog();

            ClaudeCliOptions options = ClaudeCliOptions.FromEnvironment(
                log, Env(ClaudeCliOptions.ModelEnvVar, "haiku; rm -rf /"));

            Assert.Equal(ClaudeCliOptions.DefaultModel, options.Model);
            Assert.Contains(log.Lines, line => line.Contains(ClaudeCliOptions.ModelEnvVar));
        }

        [Fact]
        public void EnvironmentUnset_LeavesEveryDefaultStanding()
        {
            ClaudeCliOptions options = ClaudeCliOptions.FromEnvironment(null, name => null);

            Assert.Equal(ClaudeCliOptions.DefaultModel, options.Model);
            Assert.Equal(120, options.TimeoutSeconds);
            Assert.Null(options.ExecutablePath);
        }

        private static Func<string, string> Env(string name, string value)
        {
            var map = new Dictionary<string, string> { { name, value } };
            return key =>
            {
                string? found;
                return map.TryGetValue(key, out found) ? found : string.Empty;
            };
        }

        private static int CountQuotes(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"') count++;
            }
            return count;
        }

        private sealed class RecordingLog : IFlavorLog
        {
            public List<string> Lines { get; } = new List<string>();

            public void Debug(string message) { Lines.Add(message); }
            public void Info(string message) { Lines.Add(message); }
            public void Warn(string message) { Lines.Add(message); }
            public void Error(string message, Exception? exception = null) { Lines.Add(message); }
        }
    }
}
