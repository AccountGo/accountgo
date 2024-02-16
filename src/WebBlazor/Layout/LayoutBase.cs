using Microsoft.AspNetCore.Components;
using WebBlazor.Rx;

namespace WebBlazor.Layout
{
    public class LayoutBase : LayoutComponentBase , IDisposable
    {
        [Inject]
        protected LoaderRx loaderRx { get; set; } = default!;
        protected  List<IDisposable> reactiveSubscriptions = new();
        protected bool IsLoading { get; set; }
        protected override void OnInitialized()
        {
            reactiveSubscriptions.Add(loaderRx.IsLoading.Subscribe((loading) =>
            {
                IsLoading = loading;
                StateHasChanged();
            }));

            base.OnInitialized();
        }

        public void Dispose() => reactiveSubscriptions.ForEach(sub => sub.Dispose());
    }
}
