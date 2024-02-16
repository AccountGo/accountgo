using System.Reactive.Subjects;

namespace WebBlazor.Rx
{
    public class LoaderRx
    {
        public BehaviorSubject<bool> IsLoading { get; } = new(false);
    }
}
