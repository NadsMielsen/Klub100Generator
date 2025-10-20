/*
using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using CliWrap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Klub100Cli
{
    internal class Program
    {
        public static async Task<int> Main()
        {
            return await new CliApplicationBuilder()
                .AddCommandsFromThisAssembly()
                .Build()
                .RunAsync((IReadOnlyList<string>)Downloader.Merge(AppContext.BaseDirectory));
        }
    }


    [Command("Merge")]
    public class Merge : ICommand
    {
        [CommandOption("basePath", 'b', Description = "Base path for all the files")]
        public string BasePath { get; init; } = AppContext.BaseDirectory;

        public async ValueTask ExecuteAsync(IConsole console)
        {
            await Downloader.Merge(BasePath);
        }
    }

    [Command("Cut")]
    public class Cut : ICommand
    {
        [CommandOption("basePath", 'b', Description = "Base path for all the files")]
        public string BasePath { get; init; } = AppContext.BaseDirectory;

        [CommandOption("csvFilesName", 'n', Description = "Name of the csv file")]
        public string CsvName { get; init; }

        private List<string> _urls = new List<string>();
        private List<string> _timeStamps = new List<string>();

        public async ValueTask ExecuteAsync(IConsole console)
        {
            var fileName = CsvName ?? "songs";
            var songCSV = new FileInfo($"{BasePath}\\{fileName}.csv");
            await CreateSongDict(songCSV);
        }

        public async Task CreateSongDict(FileInfo csvFile)
        {
            Console.WriteLine("Creating song dict");
            Char[] delimiters = { ',', '\n' };
            var text = File.ReadAllText(csvFile.FullName).Split(delimiters);

            for (int i = 0; i < text.Length; i++)
            {
                if (i % 2 == 0)
                {
                    _urls.Add(text[i]);

                }
                else
                {
                    _timeStamps.Add(text[i]);
                }
            }
            await Downloader.CutAudio(BasePath, _timeStamps);
        }

    }

    [Command("start")]
    public class Generator : ICommand
    {
        [CommandOption("basePath", 'b', Description = "Base path for all the files")]
        public string BasePath { get; init; } = AppContext.BaseDirectory;

        [CommandOption("csvFilesName", 'n', Description = "Name of the csv file")]
        public string CsvName { get; init; }

        [CommandOption("StartIndex", 's', Description = "Index to start downloading at")]
        public int StartIndex { get; init; } = 0;

        [CommandOption("OnlyOne", 'o', Description = "Downloads only 1 song at the start index")]
        public bool OnlyOne { get; init; } = false;

        private List<string> _urls = new List<string>();
        private List<string> _timeStamps = new List<string>();

        public async ValueTask ExecuteAsync(IConsole console)
        {
            CreateFolders();
            var fileName = CsvName ?? "songs";
            var songCSV = new FileInfo($"{BasePath}\\{fileName}.csv");
            if (songCSV.Exists)
            {
                await CreateSongDict(songCSV);
            }
            else
            {
                var originalColor = Console.ForegroundColor;
                console.ForegroundColor = ConsoleColor.Red;
                console.Output.WriteLine($"Could not find '{fileName}.csv'");
                console.ForegroundColor = originalColor;
            }

        }

        public async Task CreateSongDict(FileInfo csvFile)
        {
            Console.WriteLine("Creating song dict");
            char[] delimiters = { ',', '\n' };
            var text = File.ReadAllText(csvFile.FullName).Split(delimiters);

            for (int i = 0; i < text.Length; i++)
            {
                if (i % 2 == 0)
                {
                    _urls.Add(text[i]);

                }
                else
                {
                    _timeStamps.Add(text[i]);
                }
            }
            await Downloader.DownloadSongs(_urls, _timeStamps, BasePath, StartIndex, OnlyOne);
        }

        public void CreateFolders()
        {
            Directory.CreateDirectory(Path.Combine(BasePath, "temp"));
            Directory.CreateDirectory(Path.Combine(BasePath, "temp", "songs"));
            Directory.CreateDirectory(Path.Combine(BasePath, "temp", "songs", "trimmed"));
            Directory.CreateDirectory(Path.Combine(BasePath, "temp", "cheers"));
            Directory.CreateDirectory(Path.Combine(BasePath, "temp", "cheers", "encoded"));
            Directory.CreateDirectory(Path.Combine(BasePath, "temp", "done"));
        }

    }

    public static class Downloader
    {
        public static async Task DownloadSongs(List<string> urls, List<string> timeStamps, string basePath, int startIndex, bool onlyOne)
        {
            if (startIndex > urls.Count) return;
            var processes = new List<Process>();

            for (int i = startIndex; i < urls.Count; i++)
            {

                Console.WriteLine($"Downloading audio {urls[i]}");
                Process p = new Process();
                processes.Add(p);
                p.StartInfo.FileName = "C:\\Windows\\system32\\cmd.exe";
                p.StartInfo.WorkingDirectory = $"{basePath}";
                p.StartInfo.Arguments = $"/C yt-dlp -x --audio-format mp3 -o \\Songs\\{i}.mp3 {urls[i]}";
                if (!p.Start())
                {
                    Console.WriteLine("Failed");
                }
                p.Exited += (sender, args) =>
                {
                    processes.Remove(p);
                };
                if (onlyOne) return;
            }
        }

        public static async Task CutAudio(string basePath, List<string> timeStamps)
        {
            var songFiles = Directory.GetFiles(basePath + "\\songs").Select(f => new FileInfo(f)).ToList();
            var processes = new List<Process>();

            for (int i = 0; i < songFiles.Count; i++)
            {
                var index = int.Parse(songFiles[i].Name.Split(".")[0]);
                string command = $"/C ffmpeg -ss {timeStamps[index]} -t 59 -i songs\\{songFiles[i].Name} songs\\trimmed\\{songFiles[i].Name.Split(".")[0]}-cut.mp3";
                Console.WriteLine($"Cutting audio {songFiles[i]}");
                Console.WriteLine("Command:\n" + command);

                Process p = new Process();
                processes.Add(p);
                p.StartInfo.FileName = "C:\\Windows\\system32\\cmd.exe";
                p.StartInfo.WorkingDirectory = $"{basePath}";

                p.StartInfo.Arguments = command;
                if (!p.Start())
                {
                    Console.WriteLine("Failed");
                }
                await p.WaitForExitAsync();
            }

        }

        public static async Task Merge(string basePath)
        {
            var songFiles = Directory.GetFiles(basePath + "\\songs\\trimmed").Select(f => new FileInfo(f));
            Random rand = new Random();
            songFiles = songFiles.OrderBy(_ => rand.Next()).ToList();

            var cheers = Directory.GetFiles(basePath + "\\cheers").Select(f => new FileInfo(f)).ToList();

            File.Create(basePath + "\\songList.txt").Dispose();
            var lines = new List<string>();
            var totalFiles = songFiles.Count() - 1;
            foreach (var songFile in songFiles)
            {
                lines.Add($"file 'songs\\trimmed\\{songFile.Name}'");
                lines.Add("file 'cheers\\Airhorn.mp3'");
                lines.Add("file 'cheers\\Cheer.mp3'");
                totalFiles--;
            }
            File.WriteAllLines(basePath + "\\songList.txt", lines);

            Process p = new Process();
            p.StartInfo.FileName = "C:\\Windows\\system32\\cmd.exe";
            p.StartInfo.WorkingDirectory = $"{basePath}";
            p.StartInfo.Arguments = $"/C ffmpeg -f concat -safe 0 -i songList.txt -codec mp3 done/klub100{DateTime.Now.Ticks}.mp3";
            p.Start();
        }
    }

}*/