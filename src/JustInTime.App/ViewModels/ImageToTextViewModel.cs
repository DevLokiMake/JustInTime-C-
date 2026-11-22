using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;
using JustInTime.Core.Localization;

namespace JustInTime.App.ViewModels;

/// <summary>Окно «Фото в текст»: картинка слева, распознанный и редактируемый текст справа.</summary>
public sealed partial class ImageToTextViewModel : ObservableObject
{
    private readonly ITextRecognizer _recognizer;
    private readonly IImageInputService _images;
    private readonly ILogger<ImageToTextViewModel> _logger;

    private byte[]? _lastImage;
    private int _runId;

    public ImageToTextViewModel(
        ITextRecognizer recognizer, IImageInputService images, ILogger<ImageToTextViewModel> logger)
    {
        _recognizer = recognizer;
        _images = images;
        _logger = logger;

        var languages = new List<Option<string?>> { new(null, Loc.T("img_auto")) };

        try
        {
            languages.AddRange(recognizer.AvailableLanguages.Select(l => new Option<string?>(l.Tag, l.DisplayName)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить список языков распознавания");
        }

        Languages = languages;
        _selectedLanguage = languages[0];
    }

    public IReadOnlyList<Option<string?>> Languages { get; }

    [ObservableProperty]
    private Option<string?>? _selectedLanguage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoPreview))]
    private ImageSource? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteImageCommand))]
    private bool _isBusy;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    public bool NoPreview => Preview is null;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>Куда отдать готовый текст. Если null, кнопки «Вставить в сообщение» нет.</summary>
    public Action<string>? InsertCallback
    {
        get => _insertCallback;
        set
        {
            _insertCallback = value;
            OnPropertyChanged(nameof(CanInsert));
        }
    }

    private Action<string>? _insertCallback;

    public bool CanInsert => _insertCallback is not null;

    /// <summary>Окно просит закрыть себя (после вставки текста).</summary>
    public event Action? CloseRequested;

    partial void OnSelectedLanguageChanged(Option<string?>? value)
    {
        // Другой язык: распознаём ту же картинку заново.
        if (_lastImage is not null)
        {
            _ = RecognizeAsync(_lastImage, value?.Value);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task PickFileAsync() => LoadAsync(_images.PickFile());

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task PasteImageAsync() => LoadAsync(_images.FromClipboard());

    private bool CanLoad() => !IsBusy;

    /// <summary>Файл, перетащенный в окно.</summary>
    public Task RecognizeFileAsync(string path) => IsBusy ? Task.CompletedTask : LoadAsync(_images.ReadFile(path));

    [RelayCommand]
    private void Copy() => CopyToClipboard(ResultText.Trim());

    [RelayCommand]
    private void CopyOneLine() => CopyToClipboard(ToOneLine());

    [RelayCommand]
    private void Insert()
    {
        if (_insertCallback is null || !HasResult)
        {
            return;
        }

        _insertCallback(ToOneLine());
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        _runId++;
        _lastImage = null;
        Preview = null;
        ResultText = string.Empty;
        ShowStatus(null, false);
    }

    private string ToOneLine() => OcrTextNormalizer.ToSingleLine(ResultText.Split('\n'));

    private void CopyToClipboard(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var error = _images.CopyText(text);
        ShowStatus(error ?? Loc.T("img_copied"), error is not null);
    }

    private Task LoadAsync(ImageInputResult input)
    {
        if (input.Cancelled)
        {
            return Task.CompletedTask;
        }

        if (input.Bytes is null)
        {
            ShowStatus(input.Error ?? Loc.T("img_err_no_image"), true);
            return Task.CompletedTask;
        }

        _lastImage = input.Bytes;
        Preview = ImagePreview.Create(input.Bytes);
        return RecognizeAsync(input.Bytes, SelectedLanguage?.Value);
    }

    private async Task RecognizeAsync(byte[] image, string? languageTag)
    {
        var run = ++_runId;
        IsBusy = true;
        ShowStatus(Loc.T("img_recognizing"), false);

        try
        {
            var result = await _recognizer.RecognizeAsync(image, languageTag);

            if (run != _runId)
            {
                return;   // за это время выбрали другую картинку или язык
            }

            if (!result.Success)
            {
                ResultText = string.Empty;
                ShowStatus(result.Error ?? Loc.T("img_err_recognize"), true);
                return;
            }

            ResultText = string.Join(Environment.NewLine, result.Lines);
            ShowStatus(
                Loc.T("img_result_info", result.Language, result.Lines.Count),
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось распознать текст на изображении");

            if (run == _runId)
            {
                ShowStatus(Loc.T("img_err_recognize_ex", ex.Message), true);
            }
        }
        finally
        {
            if (run == _runId)
            {
                IsBusy = false;
            }
        }
    }

    private void ShowStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }
}
