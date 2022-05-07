using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace WpfHighlighter
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class Command1
    {
        public static readonly TimeSpan TotalDuration = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("9dad0b27-6ca5-4497-951b-8f8003aae5ad");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="Command1"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private Command1(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static Command1 Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in Command1's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new Command1(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var mainWindow = System.Windows.Application.Current.MainWindow;
            var mousePosition = Mouse.GetPosition(mainWindow);
            var hittest = VisualTreeHelper.HitTest(mainWindow, mousePosition);
            if (hittest is null)
            {
                return;
            }
            var hitVisual = hittest.VisualHit;

            var parentControl = FindParentOfType<System.Windows.Controls.Control>(hitVisual);
            if (parentControl is object)
            {
                int children = VisualTreeHelper.GetChildrenCount(mainWindow);
                var adornerDecorator = (AdornerDecorator)VisualTreeHelper.GetChild(mainWindow, children - 1);

                var adornerLayer = AdornerLayer.GetAdornerLayer(adornerDecorator.Child);
                var a = new CustomAdorner(adornerDecorator.Child, parentControl);
                adornerLayer.Add(a);
                DispatcherTimer dt = new DispatcherTimer();
                dt.Tick += (o, ev) =>
                {
                    adornerLayer.Remove(a);
                    dt.Stop();
                };
                dt.Interval = TotalDuration;
                dt.Start();
            }
        }

        private static T FindParentOfType<T>(DependencyObject visual) where T : Visual
        {
            if (visual is null) return default(T);
            if (typeof(T).IsAssignableFrom(visual.GetType()))
                return (T)visual;
            return FindParentOfType<T>(VisualTreeHelper.GetParent(visual));
        }
    }

    internal class CustomAdorner : Adorner
    {
        private readonly UIElement highlightElement;

        public CustomAdorner(UIElement adornedElement, UIElement highlightElement) : base(adornedElement)
        {
            this.highlightElement = highlightElement;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            Size s = this.AdornedElement.RenderSize;



            var t = this.highlightElement.TransformToVisual(this.AdornedElement);
            var innerRect = new Rect(new Point(0, 0), this.highlightElement.RenderSize);
            innerRect = t.TransformBounds(innerRect);
            // scale up to allow a thickness/2 border


            var r1 = new RectangleGeometry(new Rect(0, 0, s.Width, s.Height));
            var r2 = new RectangleGeometry(innerRect);

            var cg = new CombinedGeometry(r1, r2);
            cg.GeometryCombineMode = GeometryCombineMode.Exclude;
            var cg2 = CombinedGeometry.Combine(r1, r2, GeometryCombineMode.Exclude, Transform.Identity);

            var brush = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128));
            Pen pen = null;
            drawingContext.DrawGeometry(brush, pen, cg2);

            double scaleX = (innerRect.Width + 5.0) / innerRect.Width;
            double scaleY = (innerRect.Height + 5.0) / innerRect.Height;
            double x = innerRect.Left + (innerRect.Width / 2.0);
            double y = innerRect.Top + (innerRect.Height / 2.0);
            var scale = new ScaleTransform(scaleX, scaleY, x, y);
            innerRect = scale.TransformBounds(innerRect);

            Rect startingRect = innerRect;
            double maxScaleX = (startingRect.Width + 5) / startingRect.Width;
            double maxScaleY = (startingRect.Height + 5) / startingRect.Height;
            var maxScale = new ScaleTransform(maxScaleX, maxScaleY, x, y);
            Rect endRect = startingRect;
            endRect = maxScale.TransformBounds(endRect);
            RectAnimation ra = new RectAnimation(startingRect, endRect, TimeSpan.FromMilliseconds(500));
            ra.AutoReverse = true;
            ra.RepeatBehavior = RepeatBehavior.Forever;
            var ac = ra.CreateClock();

            Pen border = new Pen() { Brush = Brushes.Blue, Thickness = 5 };
            drawingContext.DrawRectangle(null, border, innerRect, ac);
        }
    }
}
