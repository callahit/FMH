using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fmh.Core;
using Fmh.Core.Logic;
using Fmh.Desktop.Services;

namespace Fmh.Desktop.ViewModels;

public sealed record PrivateRow(string Name, string Detail, bool Owned, string Action, IRelayCommand Command);

/// <summary>Privatleben (AP 13b): Privatkonto, Stress, Wohnung, Auto und Hobbys.</summary>
public sealed partial class PrivateLifeViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    public PrivateLifeViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Load();
    }

    [ObservableProperty] private string _savings = "";
    [ObservableProperty] private double _stress;
    [ObservableProperty] private string _monthly = "";
    [ObservableProperty] private string _prestige = "";
    [ObservableProperty] private string _relief = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private IReadOnlyList<PrivateRow> _homes = Array.Empty<PrivateRow>();
    [ObservableProperty] private IReadOnlyList<PrivateRow> _cars = Array.Empty<PrivateRow>();
    [ObservableProperty] private IReadOnlyList<PrivateRow> _hobbies = Array.Empty<PrivateRow>();

    public string HobbiesTitle => T.Format("private.hobbies", Data.PrivateLife?.MaxHobbies ?? 0);
    public string StressText => Fmt.Num(Stress, "0") + "/100";

    private void Load()
    {
        if (Session.PrivateLife is not { } life || Data.PrivateLife is not { } rules) return;
        string Money(long v) => Fmt.Money(v, Fmh.Core.Data.CurrencyConfig.Base);
        Savings = Money(life.Savings);
        Stress = life.Stress;
        OnPropertyChanged(nameof(StressText));
        Monthly = Money(PrivateLives.MonthlyCost(rules, life));
        Prestige = Fmt.Num(PrivateLives.Prestige(rules, life), "0.0");
        Relief = Fmt.Num(PrivateLives.Relief(rules, life), "0.00");
        List<PrivateRow> Rows(PrivateLives.Kind kind) => PrivateLives.Items(rules, kind).Select(i =>
        {
            var owned = kind switch
            {
                PrivateLives.Kind.Home => life.Home == i.Id,
                PrivateLives.Kind.Car => life.Car == i.Id,
                _ => life.Hobbies.Contains(i.Id),
            };
            var net = kind == PrivateLives.Kind.Hobby ? i.Price : PrivateLives.NetPrice(rules, life, kind, i);
            var action = kind == PrivateLives.Kind.Hobby ? T[owned ? "private.hobbyOn" : "private.hobbyOff"] : T[owned ? "private.owned" : "private.buy"];
            return new PrivateRow(T["private." + i.Id], T.Format("private.price", Money(i.Price), Money(net), Money(i.Monthly)), owned, action,
                new RelayCommand(() => Change(kind, i.Id), () => kind == PrivateLives.Kind.Hobby || !owned));
        }).ToList();
        Homes = Rows(PrivateLives.Kind.Home);
        Cars = Rows(PrivateLives.Kind.Car);
        Hobbies = Rows(PrivateLives.Kind.Hobby);
    }

    public void Change(PrivateLives.Kind kind, string id)
    {
        var error = Session.ChangePrivate(kind, id);
        Message = error is null ? "" : T[error];
        Load();
        _shell.RefreshHeader();
    }

    [RelayCommand]
    private void Close() => MainWindowViewModel.Instance?.CloseDialog();
}
