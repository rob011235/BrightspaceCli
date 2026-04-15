using System.Text;
using InsTK.Core;

namespace InsTK.Maui;

public sealed class InsTkCommandRunner
{
    private readonly WorkspaceService workspaceService;
    private PromptingTextReader? activeInput;

    public InsTkCommandRunner(WorkspaceService workspaceService)
    {
        this.workspaceService = workspaceService;
    }

    public event EventHandler<bool>? InputPendingChanged;

    public async Task<int> ExecuteAsync(IInsTkCommand command, Action<string, bool> onLog)
    {
        var snapshot = workspaceService.LoadSnapshot();
        var previousDirectory = Directory.GetCurrentDirectory();
        var host = new MauiCommandHost(onLog, HandleInputPendingChanged);
        activeInput = host.InputReader;

        try
        {
            Directory.SetCurrentDirectory(snapshot.WorkspaceRoot);
            var app = InsTkApplication.CreateDefault();
            return await Task.Run(() => app.ExecuteAsync(command, host));
        }
        finally
        {
            activeInput = null;
            HandleInputPendingChanged(this, false);
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    public void SubmitInput(string? value = "")
        => activeInput?.Submit(value);

    private void HandleInputPendingChanged(object? sender, bool isPending)
        => InputPendingChanged?.Invoke(this, isPending);

    private sealed class MauiCommandHost : ICommandHost
    {
        public MauiCommandHost(Action<string, bool> onLog, EventHandler<bool> pendingChanged)
        {
            InputReader = new PromptingTextReader(pendingChanged);
            Out = new CallbackTextWriter(line => onLog(line, false));
            Error = new CallbackTextWriter(line => onLog(line, true));
        }

        public PromptingTextReader InputReader { get; }
        public TextWriter Out { get; }
        public TextWriter Error { get; }
        public TextReader In => InputReader;
    }

    private sealed class CallbackTextWriter : TextWriter
    {
        private readonly Action<string> onLine;
        private readonly StringBuilder buffer = new();

        public CallbackTextWriter(Action<string> onLine)
        {
            this.onLine = onLine;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                FlushBuffer();
                return;
            }

            if (value != '\r')
            {
                buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            foreach (var character in value)
            {
                Write(character);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            FlushBuffer();
        }

        public override void Flush()
            => FlushBuffer();

        private void FlushBuffer()
        {
            var text = buffer.ToString();
            buffer.Clear();
            onLine(text);
        }
    }

    private sealed class PromptingTextReader : TextReader
    {
        private readonly EventHandler<bool> pendingChanged;
        private TaskCompletionSource<string?>? pendingRead;

        public PromptingTextReader(EventHandler<bool> pendingChanged)
        {
            this.pendingChanged = pendingChanged;
        }

        public override string? ReadLine()
        {
            lock (this)
            {
                pendingRead ??= new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            pendingChanged(this, true);
            return pendingRead.Task.GetAwaiter().GetResult();
        }

        public void Submit(string? value)
        {
            TaskCompletionSource<string?>? read;

            lock (this)
            {
                read = pendingRead;
                pendingRead = null;
            }

            if (read is null)
            {
                return;
            }

            read.TrySetResult(value);
            pendingChanged(this, false);
        }
    }
}
