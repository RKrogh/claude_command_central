namespace CommandCentral.Core.Configuration;

public sealed class ServerOptions
{
    public int Port { get; set; } = 9000;
    public string Host { get; set; } = "localhost";
}

public sealed class HotkeyOptions
{
    public string PttSelectedInstance { get; set; } = "Ctrl+Space";
    public string CycleInstance { get; set; } = "Ctrl+OemTilde";
    public string MuteAll { get; set; } = "Ctrl+Shift+M";
    public string ReadResponse { get; set; } = "Ctrl+Shift+{N}";
}

public sealed class SttOptions
{
    public string Engine { get; set; } = "Whisper";
    public string ModelPath { get; set; } = "./models/ggml-base.en.bin";
    public string Language { get; set; } = "en";
}

public sealed class TtsOptions
{
    public string NotificationEngine { get; set; } = "SherpaOnnx";
    public string ResponseEngine { get; set; } = "ElevenLabs";
    public Dictionary<string, VoiceOptions> Voices { get; set; } = new();
}

public sealed class VoiceOptions
{
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "SherpaOnnx";
}

public sealed class InstanceOptions
{
    public int MaxInstances { get; set; } = 9;
    public bool AutoAssignVoices { get; set; } = true;
}

public sealed class CommandCentralOptions
{
    public ServerOptions Server { get; set; } = new();
    public HotkeyOptions Hotkeys { get; set; } = new();
    public SttOptions Stt { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
    public InstanceOptions Instances { get; set; } = new();
}
