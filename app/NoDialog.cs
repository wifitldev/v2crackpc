using ServiceLib.Base;

namespace UiProto;

/// <summary>
/// Заглушка для IWindowDialog: у прототипа нет диалогов из ServiceLib,
/// но поле AppManager.Instance.WindowDialog обязано быть заполнено до любой
/// логики, которая может вызвать ShowDialogAsync.
/// </summary>
public sealed class NoDialog : IWindowDialog
{
    public Task<bool> ShowDialogAsync<TViewModel>(TViewModel vm) where TViewModel : class
        => Task.FromResult(false);
}
