using System.Diagnostics;

// 320ifier - parallel automatic bulk transcoding toolkit
// 2023-2026 Nightshade System
// warning: this program will use as many cores as it can, be careful
// ^ no it doesn't as of 15/06/2026 and whoever wrote that is wrong

// how to 320ify:
//   - have ffmpeg in your path
//   - put your files in a folder named "input"
//   - run 320ifier
//   - your freshly-baked files will be in the output folder

namespace ThreeTwentyfier;

public static class Program
{
    enum Mode
    {
        To320,
        To16Bit,
        ToV0,
        Wav2Flac
    }
    
    class WorkUnit
    {
        public ProcessStartInfo Info = null!;
        public int ThreadId;
        public string FileName = "";
        public int WorkId;
    }
    
    private static int coreLimit;
    private static bool recursive;
    private static bool ultraslow;

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    // Yeah, and so fucking what if it is?
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
        var currentThread = 0;
        var currentId = 0;

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
        
        var workUnits = new List<WorkUnit>();
        foreach (var inputPath in files)
        {
            var filename = Path.GetFileNameWithoutExtension(inputPath);
            
            var fragments =
                inputPath.Split(Path.DirectorySeparatorChar).Skip(1).ToArray();
            for (var i=0; i<fragments.Length; i++)
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

            var ffmpegArgs = "";
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            switch (mode)
            {
                case Mode.To320:
                    ffmpegArgs += "-c:a libmp3lame -ab 320k -map_metadata 0 -id3v2_version 3";
                    break;
                case Mode.To16Bit:
                    ffmpegArgs += "-sample_fmt s16";
                    break;
                case Mode.ToV0:
                    ffmpegArgs += "-c:a libmp3lame -q:a 0 -map_metadata 0 -id3v2_version 3";
                    break;
                case Mode.Wav2Flac:
                    ffmpegArgs += "-c:a flac";
                    if (ultraslow)
                        ffmpegArgs += " -compression_level 12 -exact_rice_parameters 1";
                    break;
            }

            outputPath += $".{extension}";
            psi.Arguments = $"-y -i \"{fullInputPath}\" {ffmpegArgs} \"{outputPath}\"";
            workUnits.Add(new WorkUnit
            {
                Info = psi,
                ThreadId = currentThread++ % coreLimit,
                FileName = filename,
                WorkId = currentId++
            });
        }

        return workUnits;
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
                        if (mode != Mode.Wav2Flac)
                        {
                            Console.Error.WriteLine("It makes no sense to run modes that aren't wav2flac in ultraslow");
                            Environment.Exit(1);
                        }
                        Console.WriteLine("Doing it ultraslow (significantly better compression with wav2flac but slow as molasses)");
                        ultraslow = true;
                        break;
                }

                if (arg.StartsWith("cores="))
                {
                    var val = arg["cores=".Length..];
                    if (val is "all" or "fuck-me-up")
                    {
                        Console.WriteLine("All the cores? Alright, all the cores");
                        coreLimit = Environment.ProcessorCount;
                    }
                    else
                    {
                        if (!int.TryParse(val, out coreLimit))
                        {
                            Console.Error.WriteLine("Argument to cores= must be a number (or 'all'), come the hell on");
                            Environment.Exit(1);
                        }

                        if (coreLimit < 0)
                        {
                            Console.Error.WriteLine("Unless you've figured out how to produce a processor made of antimatter, I don't think a negative core count is gonna work");
                            Environment.Exit(1);
                        }
                    }
                }
            }
        }

        if (coreLimit == 0)
        {
            // default to half the cores on the machine
            coreLimit = Environment.ProcessorCount / 2;
        }

        if (!Directory.Exists(Path.Combine(Environment.CurrentDirectory, "input")))
        {
            Console.Error.WriteLine("Input directory does not exist; create it within the current working directory and put your files in there");
            Environment.Exit(1);
        }

        var workUnits = BuildWorkUnitList(mode);
        
        var fileCount = workUnits.Count;
        var usableCores = Math.Min(coreLimit, fileCount);

        if (fileCount == 0)
        {
            Console.Error.WriteLine("Was not able to find any files to transcode, nothing to do");
            Environment.Exit(1);
        }
        
        Console.WriteLine($"Starting {usableCores} threads");
        
        var complete = 0;
        var threads = new List<Thread>();
        var timeAtStart = Stopwatch.GetTimestamp();
        
        for (var i = 0; i < usableCores; i++)
        {
            var threadId = i;
            var thread = new Thread(() =>
            {
                var done = new List<int>();
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
                Console.WriteLine($"Thread {threadId} completed its work pool");
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