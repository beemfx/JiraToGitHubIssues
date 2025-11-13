// (c) 2025 Beem Media. All rights reserved.

using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using System.Globalization;
using System.Text;

namespace JiraToGitHubIssues
{
	using System;
	using System.Diagnostics;
	using System.IO;

	class GitHubCliCaller
	{
		public static string RunCommand(string arguments)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "gh",
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			using (Process process = new Process { StartInfo = startInfo })
			{
				process.Start();
				
				process.WaitForExit();

				string output = process.StandardOutput.ReadToEnd();
				string error = process.StandardError.ReadToEnd(); // Read error output

				if (process.ExitCode != 0)
				{
					// Handle errors if the command failed
					Console.WriteLine($"Error executing GitHub CLI command: {error}");
					throw new InvalidOperationException($"GitHub CLI command failed with exit code {process.ExitCode}. Error: {error}");
				}

				return output;
			}
		}

		public static string SimComand(string cmd)
		{
			Console.WriteLine(cmd);
			return "TestUrl";
		}
	}

	class JiraIssueData
	{
		[Name("Summary")]
		public string Summary { get; set; } = "";

		[Name("Issue key")]
		public string IssueKey { get; set; } = "";

		[Name("Issue Type")]
		public string IssueType { get; set; } = "";

		[Name("Status")]
		public string Status { get; set; } = "";

		[Name("Priority")]
		public string Priority { get; set; } = "";

		[Name("Resolution")]
		public string Resolution { get; set; } = "";

		[Name("Created")]
		public string CreatedDate { get; set; } = "";

		[Name("Description")]
		public string Description { get; set; } = "";

		// Looks like I accidentally put Descriptions in the Environment field on some, so that
		// needs to be accounted for.
		[Name("Environment")]
		public string Environment { get; set; } = "";

		public List<string> Comments { get; set; } = new();
	}

	sealed class JiraIssueMap : ClassMap<JiraIssueData>
	{
		public JiraIssueMap()
		{
			AutoMap(CultureInfo.InvariantCulture);
			Map(m => m.Comments).Name("Comment");
		}
	}

	class GitHubIssueComment
	{
		public string Body = "";
		public string BodyFilename = "";
	}

	class GitHubIssueData
	{
		public string Title = "";
		public string Label = "";
		public string Body = "";
		public string BodyFilename = "";
		public List<GitHubIssueComment> Comments = new();
		public bool bIsClosed = false;
	}

	class CsvProcessor
	{
		private string _InputFilename = "";
		private string _TmpDir = "";
		private string _repoName = "";
		private List<GitHubIssueData> _GitHubIssues = new();

		public CsvProcessor(string[] args)
		{
			ParseArgs(args);
			EnsureOutputDirectory();
			LoadDatabase();
			WriteDebugDatabase();
			PushToGitHub();
		}

		private void PushToGitHub()
		{
			foreach (var item in _GitHubIssues)
			{
				PushIssueToGitHub(item);
			}
		}

		private void PushIssueToGitHub(GitHubIssueData issue)
		{
			Console.WriteLine($"Pushing \"{issue.Title}\"...");

			File.WriteAllText(issue.BodyFilename, issue.Body);
			string CreateCmd = $"issue create --title \"{issue.Title}\" --label \"{issue.Label}\" --body-file \"{issue.BodyFilename}\" -R \"{_repoName}\"";
			string IssueUrl = GitHubCliCaller.RunCommand(CreateCmd).Replace("\n", "");

			if (IssueUrl == null || IssueUrl.Length == 0)
			{
				// It seems sometimes issue create fails, but there is no error code. An empty URL
				// then occurs.
				throw new Exception("Issue URL not created.");
			}

			if (issue.Comments.Count > 0)
			{
				Console.WriteLine("Adding " + issue.Comments.Count + " comments...");
			}

			foreach (var comment in issue.Comments)
			{
				File.WriteAllText(comment.BodyFilename, comment.Body);
				string AddCommentCmd = $"issue comment \"{IssueUrl}\" --body-file \"{comment.BodyFilename}\"";
				GitHubCliCaller.RunCommand(AddCommentCmd);
			}

			if (issue.bIsClosed)
			{
				Console.WriteLine($"Closing issue...");
				string CloseCmd = $"issue close \"{IssueUrl}\"";
				GitHubCliCaller.RunCommand(CloseCmd);
			}
		}

		private void WriteDebugDatabase()
		{
			StringBuilder sb = new();

			foreach (var item in _GitHubIssues)
			{
				sb.AppendLine("----------------------------------------");
				sb.AppendLine("Title: " + item.Title);
				sb.AppendLine("Open: " + (item.bIsClosed ? "No" : "Yes"));
				sb.AppendLine("Label: " + item.Label);
				sb.AppendLine("Body: " + item.BodyFilename);
				sb.Append(item.Body);
				sb.AppendLine();
				sb.AppendLine("Comments:");
				foreach (var comment in item.Comments)
				{
					sb.AppendLine("---");
					sb.AppendLine("File: " + comment.BodyFilename);
					sb.Append(comment.Body);
					sb.AppendLine();
					sb.AppendLine("---");
				}
				sb.AppendLine("----------------------------------------");
			}

			File.WriteAllText(_TmpDir + "_Issues.txt", sb.ToString());
		}

		private void LoadDatabase()
		{
			using (var reader = new StreamReader(_InputFilename, new FileStreamOptions { Access = FileAccess.Read, Share = FileShare.Read }))
			{
				var config = new CsvConfiguration(CultureInfo.InvariantCulture)
				{
					HasHeaderRecord = true,
				};

				using (var csv = new CsvReader(reader, config))
				{
					csv.Context.RegisterClassMap<JiraIssueMap>();

					foreach (var record in csv.GetRecords<JiraIssueData>())
					{
						GitHubIssueData newIssue = new();

						newIssue.Title = string.Format($"{record.IssueKey} - {record.Summary}").Replace("\"", "_");

						newIssue.Body = new Func<string>(() =>
							{
								var sb = new StringBuilder();

								sb.AppendLine("Date Filed: " + record.CreatedDate);
								if (record.Resolution != null && record.Resolution.Length > 0)
								{
									sb.AppendLine("Resolution: " + record.Resolution);
								}

								sb.AppendLine();

								// sequence of statements to build Body
								sb.Append(record.Description);

								// if Description was accidentally put in Environment for some rows
								if (sb.Length == 0 && !string.IsNullOrWhiteSpace(record.Environment))
								{
									sb.Append(" ");
								}
								sb.Append(record.Environment);

								// final returned value
								return sb.ToString();
							})();

						newIssue.Label = new Func<string>(() =>
							{
								switch (record.IssueType)
								{
									case "Bug": return "bug";
									case "Task": return "enhancement";
									case "Improvement": return "enhancement";
									case "New Feature": return "enhancement";
									case "Epic": return "enhancement";
								}

								throw new Exception("Found an issue type that is not supported: " + record.IssueType);
							})();

						newIssue.bIsClosed = new Func<bool>(() =>
							{
								switch (record.Status)
								{
									case "To Do": return false;
									case "Done": return true;
								}

								throw new Exception("Found a status type that is not supported: " + record.Status);
							})();

						newIssue.BodyFilename = _TmpDir + record.IssueKey + ".txt";

						int commentIdx = 0;
						foreach (var comment in record.Comments)
						{
							if (comment != null && comment.Length > 0)
							{
								newIssue.Comments.Add(new GitHubIssueComment { Body = ParseComment(comment), BodyFilename = _TmpDir + record.IssueKey + "-comment-" + commentIdx + ".txt" });
								commentIdx++;
							}
						}

						_GitHubIssues.Add(newIssue);
					}
				}
			}
		}

		private static string ParseComment(string comment)
		{
			StringBuilder sb = new();

			string[] SplitComment = comment.Split(';', 3, StringSplitOptions.TrimEntries);
			if (SplitComment.Length < 3)
			{
				throw new Exception("Improper comment found: " + comment);
			}

			sb.AppendLine("Date: " + SplitComment[0]);
			sb.AppendLine();
			sb.Append(SplitComment[2]);

			return sb.ToString();
		}

		private void EnsureOutputDirectory()
		{
			if (_TmpDir.Length == 0)
			{
				_TmpDir = Path.GetTempPath() + "J2GH";
			}

			Directory.CreateDirectory(_TmpDir);

			if (!Directory.Exists(_TmpDir))
			{
				throw new Exception("Temporary directory not available.");
			}

			// Make sure _TempDir ends with a backslash
			if (!_TmpDir.EndsWith("\\"))
			{
				_TmpDir += "\\";
			}
		}

		private void ParseArgs(string[] args)
		{
			bool bNextIsInputFilename = false;
			bool bNextIsTmpDir = false;
			bool bNextIsRepoName = false;

			foreach (string arg in args)
			{
				if (bNextIsInputFilename)
				{
					bNextIsInputFilename = false;
					_InputFilename = arg;
				}
				else if (bNextIsTmpDir)
				{
					bNextIsTmpDir = false;
					_TmpDir = arg;
				}
				else if (bNextIsRepoName)
				{
					bNextIsRepoName = false;
					_repoName = arg;
				}
				else
				{
					if (arg == "-in")
					{
						bNextIsInputFilename = true;
					}
					else if (arg == "-t")
					{
						bNextIsTmpDir = true;
					}
					else if (arg == "-repo")
					{
						bNextIsRepoName = true;
					}
				}
			}
		}
	}

	internal class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("JiraToGitHubIssues (c) 2025 Beem Media. All rights reserved.");

			try
			{
				new CsvProcessor(args);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Failed: " + ex.ToString());
				Debugger.Break();
			}
		}
	}
}
