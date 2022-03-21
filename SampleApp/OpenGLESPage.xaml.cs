using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using GLUWP;
using Windows.UI.Popups;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace SampleApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class OpenGLESPage : Page
    {
        private OpenGLES mOpenGLES;

        private EGLSurface mRenderSurface; // This surface is associated with a swapChainPanel on the page
        private object mRenderSurfaceCriticalSection = new object();
        private IAsyncAction mRenderLoopWorker;

        public OpenGLESPage() : this(null)
        {
        }

        internal OpenGLESPage(OpenGLES openGLES)
        {
            mOpenGLES = openGLES;
            mRenderSurface = EGL.NO_SURFACE;
            InitializeComponent();

            // NOTE: assets must have build action set to 'content' to be found.

            var fp = new CartoType.FrameworkParam();
            fp.m_map_filename = "Assets\\isle_of_wight.ctm1";
            bool b = File.Exists(fp.m_map_filename);
            fp.m_style_sheet_filename = "Assets\\standard.ctstyle";
            b = File.Exists(fp.m_style_sheet_filename);
            fp.m_font_filename = "Assets\\DejaVuSans.ttf";
            b = File.Exists(fp.m_font_filename);
            m_framework = new CartoType.Framework(fp);
            m_framework.SetAnimateTransitions(true);
            var metadata = m_framework.GetMapMetaData(0);

            Windows.UI.Core.CoreWindow window = Windows.UI.Xaml.Window.Current.CoreWindow;

            window.VisibilityChanged += new TypedEventHandler<CoreWindow, VisibilityChangedEventArgs>((win, args) => OnVisibilityChanged(win, args));

            Loaded += (sender, args) => OnPageLoaded(sender, args);
            Unloaded += (sender, args) => OnPageUnloaded(sender, args);

            PointerPressed += new PointerEventHandler(OnPointerPressed);
            PointerReleased += new PointerEventHandler(OnPointerReleased);
            PointerMoved += new PointerEventHandler(OnPointerMoved);
            PointerWheelChanged += new PointerEventHandler(OnPointerWheelChanged);
            KeyDown += new KeyEventHandler(OnKeyDown);
        }

        void OnPointerPressed(object aSender, PointerRoutedEventArgs aEvent)
        {
            Windows.UI.Input.PointerPoint p = aEvent.GetCurrentPoint(this);
            if (p.Properties.IsLeftButtonPressed)
            {
                m_map_drag_anchor_x = p.Position.X;
                m_map_drag_anchor_y = p.Position.Y;
                aEvent.Handled = true;
            }
            else if (p.Properties.IsRightButtonPressed)
            {
                var p2 = new CartoType.Point(p.Position.X, p.Position.Y);
                m_framework.ConvertPoint(p2, CartoType.CoordType.Screen, CartoType.CoordType.Degree);

                if (m_last_point.X != 0 && m_last_point.Y != 0)
                {
                    var cs = new CartoType.RouteCoordSet();
                    cs.m_coord_type = CartoType.CoordType.Degree;
                    var rp = new CartoType.RoutePoint();
                    rp.m_x = m_last_point.X;
                    rp.m_y = m_last_point.Y;
                    cs.m_route_point_list.Add(rp);
                    rp = new CartoType.RoutePoint();
                    rp.m_x = p2.X;
                    rp.m_y = p2.Y;
                    cs.m_route_point_list.Add(rp);
                    var result = m_framework.StartNavigation(cs);
                    if (result != CartoType.Result.Success)
                    {
                        var s = CartoType.Util.ErrorString(result);
                        var m = new MessageDialog("Error: " + s);
                        m.ShowAsync();
                    }
                }
                m_last_point = p2;
            }
        }
        void OnPointerReleased(object aSender, PointerRoutedEventArgs aEvent)
        {
            Windows.UI.Input.PointerPoint p = aEvent.GetCurrentPoint(this);
            if (p.Properties.IsLeftButtonPressed)
            {
                aEvent.Handled = true;
            }
        }

        void OnPointerMoved(object aSender, PointerRoutedEventArgs aEvent)
        {
            Windows.UI.Input.PointerPoint p = aEvent.GetCurrentPoint(this);
            if (p.Properties.IsLeftButtonPressed)
            {
                double dx = Math.Round(m_map_drag_anchor_x - p.Position.X);
                double dy = Math.Round(m_map_drag_anchor_y - p.Position.Y);
                m_framework.Pan((int)(dx),(int)(dy));
                m_map_drag_anchor_x = p.Position.X;
                m_map_drag_anchor_y = p.Position.Y;
                aEvent.Handled = true;
            }
        }

        void OnPointerWheelChanged(object aSender, PointerRoutedEventArgs aEvent)
        {
            Windows.UI.Input.PointerPoint p = aEvent.GetCurrentPoint(this);
            int zoom_count = p.Properties.MouseWheelDelta / 120;
            double zoom = Math.Sqrt(2);
            if (zoom_count == 0)
                zoom_count = p.Properties.MouseWheelDelta >= 0 ? 1 : -1;
            zoom = Math.Pow(zoom, zoom_count);
            var r = new CartoType.Rect();
            m_framework.GetView(r, CartoType.CoordType.Screen);
            if (p.Position.X >= 0 && p.Position.X < r.MaxX && p.Position.Y >= 0 && p.Position.Y < r.MaxY)
                m_framework.ZoomAt(zoom, p.Position.X, p.Position.Y, CartoType.CoordType.Screen);
            else
                m_framework.Zoom(zoom);
            aEvent.Handled = true;
        }

        void OnKeyDown(object aSender,KeyRoutedEventArgs aEvent)
        {
            switch (aEvent.Key)
            {
                case Windows.System.VirtualKey.P:
                    m_framework.SetPerspective(!m_framework.Perspective());
                    break;
                    
                case Windows.System.VirtualKey.N:
                    m_framework.SetNightMode(!m_framework.NightMode());
                    break;

                case Windows.System.VirtualKey.R:
                    m_framework.Rotate(10);
                    break;

                case Windows.System.VirtualKey.L:
                    m_framework.Rotate(-10);
                    break;
            }

            aEvent.Handled = true;            
        }

        ~OpenGLESPage()
        {
            StopRenderLoop();
            m_renderer = null;
            DestroyRenderSurface();
        }

        private void OnPageLoaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // The SwapChainPanel has been created and arranged in the page layout, so EGL can be initialized.
            CreateRenderSurface();
            StartRenderLoop();
        }

        private void OnPageUnloaded(object aSender, Windows.UI.Xaml.RoutedEventArgs aEvent)
        {
            StopRenderLoop();
            m_renderer = null;
            DestroyRenderSurface();
        }

        private void OnVisibilityChanged(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.VisibilityChangedEventArgs args)
        {
            if (args.Visible && mRenderSurface != EGL.NO_SURFACE)
            {
                StartRenderLoop();
            }
            else
            {
                StopRenderLoop();
            }
        }

        private void CreateRenderSurface()
        {
            if (mOpenGLES != null && mRenderSurface == EGL.NO_SURFACE)
            {
                // The app can configure the the SwapChainPanel which may boost performance.
                // By default, this template uses the default configuration.
                mRenderSurface = mOpenGLES.CreateSurface(swapChainPanel, null, null);

                // You can configure the SwapChainPanel to render at a lower resolution and be scaled up to
                // the swapchain panel size. This scaling is often free on mobile hardware.
                //
                // One way to configure the SwapChainPanel is to specify precisely which resolution it should render at.
                // Size customRenderSurfaceSize = Size(800, 600);
                // mRenderSurface = mOpenGLES->CreateSurface(swapChainPanel, &customRenderSurfaceSize, nullptr);
                //
                // Another way is to tell the SwapChainPanel to render at a certain scale factor compared to its size.
                // e.g. if the SwapChainPanel is 1920x1280 then setting a factor of 0.5f will make the app render at 960x640
                // float customResolutionScale = 0.5f;
                // mRenderSurface = mOpenGLES->CreateSurface(swapChainPanel, nullptr, &customResolutionScale);
                // 
            }
        }

        private void DestroyRenderSurface()
        {
            if (mOpenGLES != null)
            {
                mOpenGLES.DestroySurface(mRenderSurface);
            }

            mRenderSurface = EGL.NO_SURFACE;
        }

        void RecoverFromLostDevice()
        {
            // Stop the render loop, reset OpenGLES, recreate the render surface
            // and start the render loop again to recover from a lost device.

            StopRenderLoop();

            {
                lock (mRenderSurfaceCriticalSection)
                {
                    DestroyRenderSurface();
                    mOpenGLES.Reset();
                    CreateRenderSurface();
                }
            }

            StartRenderLoop();
        }

        void StartRenderLoop()
        {
            // If the render loop is already running then do not start another thread.
            if (mRenderLoopWorker != null && mRenderLoopWorker.Status == Windows.Foundation.AsyncStatus.Started)
            {
                return;
            }

            // Create a task for rendering that will be run on a background thread.
            var workItemHandler =
                new Windows.System.Threading.WorkItemHandler(action =>
            {
                lock (mRenderSurfaceCriticalSection)
                {
                    mOpenGLES.MakeCurrent(mRenderSurface);
                    m_renderer = new CartoType.MapRenderer(m_framework);
                    
                    while (action.Status == Windows.Foundation.AsyncStatus.Started)
                    {
                        int panelWidth = 0;
                        int panelHeight = 0;
                        mOpenGLES.GetSurfaceDimensions(mRenderSurface, ref panelWidth, ref panelHeight);

                        m_framework.Resize(panelWidth, panelHeight);
                        
                        // DRAW THE MAP.
                        m_renderer.Draw();

                        // The call to eglSwapBuffers might not be successful (i.e. due to Device Lost)
                        // If the call fails, then we must reinitialize EGL and the GL resources.
                        if (mOpenGLES.SwapBuffers(mRenderSurface) != EGL.TRUE)
                        {
                            // XAML objects like the SwapChainPanel must only be manipulated on the UI thread.
                            swapChainPanel.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High,
                                new Windows.UI.Core.DispatchedHandler(() =>
                            {
                                RecoverFromLostDevice();
                            }));

                            return;
                        }
                    }
                }
            });

            // Run task on a dedicated high priority background thread.
            mRenderLoopWorker = Windows.System.Threading.ThreadPool.RunAsync(workItemHandler,
                Windows.System.Threading.WorkItemPriority.High,
                Windows.System.Threading.WorkItemOptions.TimeSliced);
        }

        void StopRenderLoop()
        {
            if (mRenderLoopWorker != null)
            {
                mRenderLoopWorker.Cancel();
                mRenderLoopWorker = null;
            }
        }

        private CartoType.Framework m_framework;
        private CartoType.MapRenderer m_renderer;
        private double m_map_drag_anchor_x;
        private double m_map_drag_anchor_y;
        private CartoType.Point m_last_point = new CartoType.Point();
    }
}
