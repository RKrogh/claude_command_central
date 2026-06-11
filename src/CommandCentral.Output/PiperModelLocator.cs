namespace CommandCentral.Output;

/// <summary>
/// Locates Piper VITS voice models in the sherpa-onnx bundle layout:
/// &lt;modelsDir&gt;/vits-piper-&lt;voice&gt;/&lt;voice&gt;.onnx + tokens.txt + espeak-ng-data/.
/// A bare &lt;modelsDir&gt;/&lt;voice&gt;/ folder is accepted too, for manually arranged models.
/// </summary>
public static class PiperModelLocator
{
    public sealed record PiperModel(string Voice, string ModelPath, string TokensPath, string DataDir);

    /// <summary>
    /// Returns the model files for a voice, or null if the voice is not
    /// downloaded (or the bundle is incomplete).
    /// </summary>
    public static PiperModel? Locate(string modelsDir, string voice)
    {
        string[] candidates =
        [
            Path.Combine(modelsDir, $"vits-piper-{voice}"),
            Path.Combine(modelsDir, voice)
        ];

        foreach (var dir in candidates)
        {
            var model = Validate(dir, voice);
            if (model is not null)
                return model;
        }

        return null;
    }

    /// <summary>
    /// Lists all complete voice models found under the models directory.
    /// </summary>
    public static IReadOnlyList<string> ListAvailableVoices(string modelsDir)
    {
        if (!Directory.Exists(modelsDir))
            return [];

        var voices = new List<string>();
        foreach (var dir in Directory.GetDirectories(modelsDir))
        {
            var name = Path.GetFileName(dir);
            var voice = name.StartsWith("vits-piper-", StringComparison.OrdinalIgnoreCase)
                ? name["vits-piper-".Length..]
                : name;

            if (Validate(dir, voice) is not null)
                voices.Add(voice);
        }

        voices.Sort(StringComparer.OrdinalIgnoreCase);
        return voices;
    }

    private static PiperModel? Validate(string dir, string voice)
    {
        var modelPath = Path.Combine(dir, $"{voice}.onnx");
        var tokensPath = Path.Combine(dir, "tokens.txt");
        var dataDir = Path.Combine(dir, "espeak-ng-data");

        return File.Exists(modelPath) && File.Exists(tokensPath) && Directory.Exists(dataDir)
            ? new PiperModel(voice, modelPath, tokensPath, dataDir)
            : null;
    }
}
