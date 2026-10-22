using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Winddy.Models;

namespace Winddy.Services;

/// <summary>
/// Command-line automation, the Windows counterpart of Maccy's Shortcuts actions:
///   Winddy.exe get [N]            print the N-th history item (default 1)
///   Winddy.exe select N [--paste] copy (and paste) the N-th item
///   Winddy.exe delete N           delete the N-th item
///   Winddy.exe clear [--all]      clear unpinned (or all) items
/// The running instance executes the command and replies over a named pipe.
/// </summary>
public static class CommandServer
{
    private static readonly string PipeName = "Winddy.Commands." + Environment.UserName;
    private static readonly string[] Verbs = { "get", "select", "delete", "clear" };

    public static bool IsCommand(string[] args) => args.Length > 0 && Verbs.Contains(args[0].ToLowerInvariant());

    // ───────────── Client ─────────────

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    /// <summary>Sends the command to the running instance, prints the reply and returns the exit code.</summary>
    public static int RunClient(string[] args)
    {
        AttachConsole(-1);
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            writer.WriteLine(string.Join('\u001f', args));
            var status = reader.ReadLine();
            var body = reader.ReadToEnd();
            if (status == "ok")
            {
                stdout.Write(body);
                return 0;
            }
            stderr.WriteLine(body.Trim());
            return 1;
        }
        catch (TimeoutException)
        {
            stderr.WriteLine("Winddy is not running.");
            return 2;
        }
    }

    // ───────────── Server ─────────────

    public static void Start(App app)
    {
        var thread = new Thread(() => Serve(app)) { IsBackground = true, Name = "Winddy commands" };
        thread.Start();
    }

    private static void Serve(App app)
    {
        while (!app.IsQuitting)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
                pipe.WaitForConnection();
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                var line = reader.ReadLine() ?? "";
                var args = line.Split('\u001f');
                string reply;
                try
                {
                    reply = "ok\n" + app.Dispatcher.Invoke(() => Execute(app, args));
                }
                catch (Exception ex)
                {
                    reply = "error\n" + ex.Message;
                }
                writer.Write(reply);
                pipe.WaitForPipeDrain();
            }
            catch (IOException)
            {
                // Client went away; keep serving.
            }
        }
    }

    private static string Execute(App app, string[] args)
    {
        var verb = args[0].ToLowerInvariant();
        var flags = args.Skip(1).Where(a => a.StartsWith("--")).Select(a => a.ToLowerInvariant()).ToHashSet();
        var numbers = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();

        HistoryItem ItemAt(int defaultNumber)
        {
            var n = numbers.Count > 0 && int.TryParse(numbers[0], out var parsed) ? parsed : defaultNumber;
            var items = app.Store.Sorted();
            if (n < 1 || n > items.Count) throw new ArgumentException($"No item number {n} (history has {items.Count}).");
            return items[n - 1];
        }

        switch (verb)
        {
            case "get":
            {
                var item = ItemAt(1);
                if (item.HasFiles) return string.Join(Environment.NewLine, item.Files!) + Environment.NewLine;
                if (item.HasImage) return Path.Combine(HistoryStore.ImagesDirectory, item.ImageFile!) + Environment.NewLine;
                return item.Text ?? "";
            }
            case "select":
            {
                var item = ItemAt(1);
                var target = Native.GetForegroundWindow();
                app.Clipboard.Copy(item, flags.Contains("--plain"));
                if (flags.Contains("--paste")) _ = ClipboardService.PasteAsync(target);
                return "";
            }
            case "delete":
                app.Store.Delete(ItemAt(1));
                return "";
            case "clear":
                app.Store.Clear(flags.Contains("--all"));
                if (AppSettings.Current.ClearSystemClipboard) ClipboardService.ClearSystemClipboard();
                return "";
            default:
                throw new ArgumentException($"Unknown command '{verb}'.");
        }
    }
}
