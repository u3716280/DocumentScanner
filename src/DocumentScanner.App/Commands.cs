using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
namespace DocumentScanner.App;
public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null)
    {if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(name);return true;}
}
public sealed class RelayCommand(Action execute,Func<bool>? can=null) : ICommand
{
    public bool CanExecute(object? parameter)=>can?.Invoke()??true;
    public void Execute(object? parameter)=>execute();
    public event EventHandler? CanExecuteChanged;
    public void Raise()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public static class StaTask
{
    public static Task<T> Run<T>(Func<Task<T>> action)
    {
        var tcs=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async()=>{
                try{tcs.TrySetResult(await action());}catch(OperationCanceledException){tcs.TrySetCanceled();}catch(Exception ex){tcs.TrySetException(ex);}
                finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}
            }));
            Dispatcher.Run();
        }){IsBackground=true,Name="Document Scanner STA operation"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;
    }
    public static Task Run(Func<Task> action)=>Run(async()=>{await action();return true;});
}
