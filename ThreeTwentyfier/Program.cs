using System.Diagnostics;

// 320ifier - parallel automatic bulk transcoding toolkit
// 2023-2026 Nightshade System
// warning: this program will use as many cores as it can, be careful
// ^ no it doesn't as of 15/06/2026 and whoever wrote that is wrong

namespace ThreeTwentyfier;

public static class Program
{
    private static int coreLimit;
    private static bool recursive;
    private static bool ultraslow;

    static List<string> RecursiveEnumerateFiles(string path, int level = 0)
    {
        if (level >= 20)
            throw new Exception("recursed too deep, there's probably a symlink in your input, get rid of it");

        var output = new List<string>();
        var dir = Directory.EnumerateFileSystemEntries(path);
        foreach (var entry in dir)
        {
            if (Directory.Exists(entry))
            {
                var subfolder = RecursiveEnumerateFiles(entry, level++);
                subfolder.ForEach(output.Add);
            } else if (File.Exists(entry))
            {
                output.Add(entry);
            }
            else
            {
                throw new Exception("What the fuck?");
            }
        }

        return output;
    }
    
    static List<WorkUnit> BuildWorkUnitList(Mode mode)
    {
        var list = new List<WorkUnit>();

        var currentThread = 0u;
        var currentId = 0u;

        IEnumerable<string> files;
        if (recursive) files = RecursiveEnumerateFiles("input");
        else files = Directory.EnumerateFiles("input");
        
        var searchExtension = mode switch
        {
            Mode.Wav2Flac => "wav",
            _ => "flac"
        };

        var outputDirectory = Path.Combine(Environment.CurrentDirectory, "output");
        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        files = files.Where(e => Path.GetExtension(e)[1..] == searchExtension);
        foreach (var inputPath in files)
        {
            var filename = Path.GetFileNameWithoutExtension(inputPath);
            
            var fragments =
                inputPath.Split(Path.DirectorySeparatorChar).Skip(1).ToArray();
            for (int i=0; i<fragments.Length; i++)
            {
                var t = Path.Combine(Environment.CurrentDirectory, "output", Path.Combine(fragments[..i]));
                if (!Directory.Exists(t)) Directory.CreateDirectory(t);
            }

            var combined = Path.Combine(fragments);
            var outputPath =
                Path.Combine("output", combined)[..^(searchExtension.Length+1)];

            var fullInputPath = Path.Combine(Environment.CurrentDirectory, inputPath);
            
            var extension = mode switch
            {
                Mode.To320 => "mp3",
                Mode.To16Bit => "flac",
                Mode.ToV0 => "mp3",
                Mode.Wav2Flac => "flac",
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            outputPath += $".{extension}";
            
            var psi = mode switch
            {
                Mode.To320 => new ProcessStartInfo
                {
                    FileName = @"ffmpeg",
                    Arguments = $"""
                                 -y -i "{fullInputPath}" -ab 320k -map_metadata 0 -id3v2_version 3 "{outputPath}"
                                 """
                },
                Mode.To16Bit => new ProcessStartInfo
                {
                    FileName = @"ffmpeg",
                    Arguments = $"""
                                 -y -i "{fullInputPath}" -sample_fmt s16 "{outputPath}"
                                 """
                },
                Mode.ToV0 => new ProcessStartInfo
                {
                    FileName = @"ffmpeg",
                    Arguments = $"""
                                 -y -i "{fullInputPath}" -c:a libmp3lame -q:a 0 -map_metadata 0 -id3v2_version 3 "{outputPath}"
                                 """
                },
                Mode.Wav2Flac => new ProcessStartInfo
                {
                    FileName = @"ffmpeg",
                    Arguments = $"""
                                 -y -i "{fullInputPath}" -c:a flac {(ultraslow ? "-compression_level 12 -exact_rice_parameters 1" : "")} "{outputPath}"
                                 """
                },
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            psi.CreateNoWindow = true;
            psi.UseShellExecute = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            list.Add(new()
            {
                Info = psi,
                ThreadId = (uint)(currentThread++ % coreLimit),
                FileName = filename,
                WorkId = currentId++
            });
        }

        return list;
    }
    
    public static void Main(string[] args)
    {
        var mode = Mode.To320;
        if (args.Length > 0)
        {
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "16bit":
                        Console.WriteLine("16-bit mode");
                        mode = Mode.To16Bit;
                        break;
                    case "v0":
                        Console.WriteLine("v0 mode");
                        mode = Mode.ToV0;
                        break;
                    case "wav2flac":
                        Console.WriteLine("wav2flac mode");
                        mode = Mode.Wav2Flac;
                        break;
                    case "recursive":
                    case "recur":
                        Console.WriteLine("Recursive mode enabled");
                        recursive = true;
                        break;
                    case "ultraslow":
                    case "i-hate-my-computer":
                        Console.WriteLine("Doing it ultraslow (significantly better compression but slow as molasses)");
                        ultraslow = true;
                        break;
                }

                if (arg.StartsWith("cores="))
                {
                    var val = arg["cores=".Length..];
                    if (val == "all" || val == "fuck-me-up")
                    {
                        Console.WriteLine("All the cores? Alright, all the cores");
                        coreLimit = Environment.ProcessorCount;
                    }
                    else
                    {
                        coreLimit = int.Parse(val);
                    }
                }
            }
        }

        if (coreLimit == 0)
        {
            // default to half the cores on the machine
            coreLimit = Environment.ProcessorCount / 2;
        }

        var workUnits = BuildWorkUnitList(mode);
        
        var fileCount = workUnits.Count;
        var usableCores = Math.Min(coreLimit, fileCount);
        
        Console.WriteLine($"Starting {usableCores} threads");
        
        var complete = 0;
        var threads = new List<Thread>();
        var timeAtStart = Stopwatch.GetTimestamp();
        
        for (uint i = 0; i < usableCores; i++)
        {
            var threadId = i;
            var thread = new Thread(() =>
            {
                var done = new List<uint>();
                while (true)
                {
                    if (workUnits.Count == 0) break;
                    
                    var psi = workUnits.FirstOrDefault(x =>
                        x.ThreadId == threadId && !done.Contains(x.WorkId));
                    if (psi == null) break;
                    
                    Console.WriteLine($"Thread {threadId} picked up '{psi.FileName}'");
                    
                    var process = Process.Start(psi.Info)!;
                    process.WaitForExit();
                    
                    done.Add(psi.WorkId);
                    
                    Console.WriteLine($"Thread {threadId} finished '{psi.FileName}' ({++complete}/{fileCount})");
                }
                Console.WriteLine($"Thread {threadId} exhausted work pool");
            });
            thread.Start();
            threads.Add(thread);
        }
        
        foreach (var t in threads) t.Join();

        var timeAtEnd = Stopwatch.GetElapsedTime(timeAtStart);

        var timeElapsed = timeAtEnd.TotalSeconds > 60
            ? $"{Math.Floor(timeAtEnd.TotalMinutes)}m:{Math.Floor(timeAtEnd.TotalSeconds % 60)}s"
            : $"{Math.Floor(timeAtEnd.TotalSeconds)} seconds";
        
        Console.WriteLine($"Complete! Processed {fileCount} files in {timeElapsed}.");
    }
}