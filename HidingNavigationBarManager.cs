using System;
using CoreGraphics;
using Foundation;
using UIKit;

namespace HidingNavigationBar.Xamarin.iOS
{
    public enum HidingNavigationBarState
    {
        Closed,
        Contracting,
        Expanding,
        Open
    }

    public enum HidingNavigationForegroundAction
    {
        Default,
        Show,
        Hide
    }
    
    public class HidingNavigationBarManager: NSObject,IUIScrollViewDelegate,IUIGestureRecognizerDelegate
    {
        public event EventHandler HidingNavigationBarManagerDidUpdateScrollViewInsets;
        public event EventHandler<HidingNavigationBarState> HidingNavigationBarManagerDidChangeState; 
        
        private readonly UIViewController _viewController;
        private UIScrollView _scrollView;
        private UIView _extensionView;

        public nfloat ExpansionResistance { get; set; }
        public nfloat ContractingResistance { get; set; }

        public UIRefreshControl RefreshControl { get; set; }

        private readonly HidingViewController _navBarController;
        private readonly HidingViewController _extensionController;
        private HidingViewController _tabBarController;

        private nfloat _topInset;
        private nfloat _previousYOffset = float.NaN;
        private nfloat _resistanceConsumed;
        private bool _isUpdatingValues;
        private readonly NSObject _applicationWillEnterForegroundNotificationToken;
        
        private HidingNavigationBarState _currentState = HidingNavigationBarState.Open;
        private HidingNavigationBarState _previousState = HidingNavigationBarState.Open;
        private bool _disposed;
        private UIPanGestureRecognizer _uiPanGestureRecognizer;

        public HidingNavigationForegroundAction OnForegroundAction { get; set; }

        public HidingNavigationBarManager(UIViewController viewController, UIScrollView scrollView)
        {
            if (viewController.NavigationController?.NavigationBar == null)
            {
                // error
            }

            OnForegroundAction = HidingNavigationForegroundAction.Default;
            
            viewController.ExtendedLayoutIncludesOpaqueBars = true;

            _viewController = viewController;
            
            _extensionController = new HidingViewController();
            viewController.View.Add(_extensionController.View);

            var navbar = viewController.NavigationController?.NavigationBar;
            _navBarController = new HidingViewController(navbar);
            _navBarController.Child = _extensionController;
            _navBarController.AlphaFadeEnable = true;
            
            Init();
            

            UpdateScrollView(scrollView);
            
            
            _navBarController.ExpandedCenter = (view => new CGPoint(view.Bounds.GetMidX(),view.Bounds.GetMidY()+(StatusBarHeight()))); 
            
            _extensionController.ExpandedCenter = view =>
            {
                var topOffset = _navBarController.ContractionAmountValue() + StatusBarHeight();
                var point = new CGPoint(view.Bounds.GetMidX(), view.Bounds.GetMidY()+topOffset);
                return point;
            }; 
            
            UpdateContentInset();
            
            _applicationWillEnterForegroundNotificationToken = UIApplication.Notifications.ObserveWillEnterForeground(ApplicationWillEnterForeGround);
            
        }

        public void UpdateScrollView(UIScrollView scrollView)
        {
            if (scrollView == null)
            {
                return;
            }
            if (_uiPanGestureRecognizer == null)
            {
                _uiPanGestureRecognizer = new UIPanGestureRecognizer(HandelePanGesture) {Delegate = this};
            }
            else
            {
                _scrollView.RemoveGestureRecognizer(_uiPanGestureRecognizer);
            }
            _scrollView = scrollView;
           _scrollView.AddGestureRecognizer(_uiPanGestureRecognizer);

        }

        public void ManageBottomBar(UIView view)
        {
            _tabBarController = new HidingViewController(view);
            _tabBarController.ContractsUpwards = false;
            _tabBarController.ExpandedCenter = uiView =>
            {
                var height = _viewController.View.Frame.Height;
                var point = new CGPoint(view.Bounds.GetMidX(), height - view.Bounds.GetMidY());

                return point;
            };
        }

        public void AddExtensionView(UIView view)
        {
            _extensionView?.RemoveFromSuperview();
            _extensionView = view;

            var bounds = view.Frame;
            bounds.Location = CGPoint.Empty;

            _extensionView.Frame = bounds;
            _extensionController.View.Frame = bounds;
            _extensionController.View.AddSubview(view);
            _extensionController.Expand();
            
            _extensionController.View.Superview.BringSubviewToFront(_extensionController.View);
            UpdateContentInset();
        }

        public void ViewWillAppear()
        {
            Expand();
        }

        public void ViewDidLayoutSubview()
        {
            UpdateContentInset();
        }

        public void ViewWillDisapear()
        {
            Expand();
        }

        public void UpdateValues()
        {
            _isUpdatingValues = true;

            bool scrolledToTop = _scrollView.ContentInset.Top == - _scrollView.ContentOffset.Y;

            if (_extensionView != null)
            {
                var frame = _extensionController.View.Frame;
                frame.Width = _extensionView.Bounds.Size.Width;
                frame.Height = _extensionView.Bounds.Size.Height;
                _extensionController.View.Frame = frame;
            }
            
            UpdateContentInset();

            if (scrolledToTop)
            {
                var offset = _scrollView.ContentOffset;
                offset.Y = -_scrollView.ContentInset.Top;
                _scrollView.ContentOffset = offset;
            }

            _isUpdatingValues = false;
        }

        public void ShouldScrollToTop()
        {
            var top = StatusBarHeight() + _navBarController.TotalHeight();
            UpdateScrollContentInsetTop(top);

            _navBarController.Snap(false);
            _tabBarController?.Snap(false);
        }
        
        public void Expand()
        {
            _navBarController.Expand();
            _tabBarController?.Expand();
            
            _previousYOffset = nfloat.NaN;
            
            HandleScrolling();
        }

        public void ApplicationWillEnterForeGround(object sender, NSNotificationEventArgs e)
        {
            switch (OnForegroundAction)
            {
                case HidingNavigationForegroundAction.Show:
                    _navBarController.Expand();
                    _tabBarController?.Expand();
                    break;
                case HidingNavigationForegroundAction.Hide:
                    _navBarController.Contract();
                    _tabBarController?.Contract();
                    break;
            }
        }

        private bool IsViewControllerVisible()
        {
            return _viewController.IsViewLoaded && _viewController.View.Window != null;
        }

        private float StatusBarHeight()
        {
            if (UIApplication.SharedApplication.StatusBarHidden)
            {
                return 0;
            }
            var statusBarSize = UIApplication.SharedApplication.StatusBarFrame.Size;
            return(float) Math.Min(statusBarSize.Width, statusBarSize.Height);
        }

        private bool ShouldHandleScrolling()
        {
            if (_scrollView.ContentOffset.Y <= -_scrollView.ContentInset.Top && _currentState == HidingNavigationBarState.Open)
            {
                return false;
            }
            if (RefreshControl!= null && RefreshControl.Refreshing)
            {
                return false;
            }
            
            var scrollFrame = _scrollView.ContentInset.InsetRect(_scrollView.Bounds);
            var scrollableAmount = _scrollView.ContentSize.Height - scrollFrame.Height;
            var scrollViewIsSuffecientlyLong = scrollableAmount > _navBarController.TotalHeight();

            return IsViewControllerVisible() && scrollViewIsSuffecientlyLong && !_isUpdatingValues;
        }
        
        private void HandleScrolling()
        {
            if (!ShouldHandleScrolling())
            {
                return;
            }

            if (!nfloat.IsNaN(_previousYOffset))
            {
                // 1 - Calculate the delta
                var deltaY = _previousYOffset - _scrollView.ContentOffset.Y;

                // 2 - Ignore any scrollOffset beyond the bounds
                var start = -_topInset;
                if (_previousYOffset<start)
                {
                    deltaY = (float)Math.Min(0, deltaY - _previousYOffset - start);
                }

                /* rounding to resolve a dumb issue with the contentOffset value */
                var end = Math.Floor(_scrollView.ContentSize.Height - _scrollView.Bounds.Height -
                                     _scrollView.ContentInset.Bottom - 0.5);
                if (_previousYOffset > end)
                {
                    deltaY = (float)Math.Max(0, deltaY - _previousYOffset + end);
                }

                // 3 - Update contracting variable
                if (Math.Abs(deltaY)>HidingViewController.Epsilon)
                {
                    if (deltaY < 0)
                    {
                        _currentState = HidingNavigationBarState.Contracting;
                    }
                    else
                    {
                        _currentState = HidingNavigationBarState.Expanding;
                    }
                }

                // 4 - Check if contracting state changed, and do stuff if so
                if (_currentState != _previousState)
                {
                    _previousState = _currentState;
                    _resistanceConsumed = 0;
                }
                
                // 5 - Apply resistance
                if (_currentState == HidingNavigationBarState.Contracting)
                {
                    var availableResistance = ContractingResistance - _resistanceConsumed;
                    _resistanceConsumed =(float) Math.Min(ContractingResistance, _resistanceConsumed - deltaY);

                    deltaY = (float)Math.Min(0, availableResistance + deltaY);
                }
                else if(_scrollView.ContentOffset.Y > -StatusBarHeight())
                {
                    var availableResistance = ExpansionResistance - _resistanceConsumed;
                    _resistanceConsumed = (float)Math.Min(ExpansionResistance, _resistanceConsumed + deltaY);

                    deltaY = (float)Math.Max(0, deltaY - availableResistance);
                }

                // 6 - Update the shyViewController
                _navBarController.UpdateYOffset(deltaY);
                _tabBarController?.UpdateYOffset(deltaY);
            }
            
            // update content Inset
            UpdateContentInset();

            _previousYOffset = _scrollView.ContentOffset.Y;
            
            // update the visible state
            var state = _currentState;
            if (_navBarController.View.Center.Equals(_navBarController.ExpandedCenterValue()) && _extensionController.View.Center.Equals(_extensionController.ExpandedCenterValue()))
            {
                _currentState = HidingNavigationBarState.Open; 
            }
            else if (_navBarController.View.Center.Equals(_navBarController.ContractedCenterValue()) && _extensionController.View.Center.Equals(_extensionController.ContractedCenterValue()))
            {
                _currentState = HidingNavigationBarState.Closed;
            }
            if (state != _currentState)
            {
                HidingNavigationBarManagerDidChangeState?.Invoke(this,_currentState);
            }
        }

        private void UpdateContentInset()
        {
            var navBarBottomY = _navBarController.View.Frame.Y / _navBarController.View.Frame.Height;
            nfloat top;
            if (!_extensionController.IsContracted())
            {
                top = _extensionController.View.Frame.Y + _extensionController.View.Bounds.Size.Height;
            }
            else
            {
                top = navBarBottomY;
            }
            
            UpdateScrollContentInsetTop(top);
        }
        
        private void UpdateScrollContentInsetTop(nfloat top)
        {
            if (_viewController.AutomaticallyAdjustsScrollViewInsets)
            {
                var contentInset = _scrollView.ContentInset;
                contentInset.Top = top;
                _scrollView.ContentInset = contentInset;
            }

            var scrollInsets = _scrollView.ScrollIndicatorInsets;
            scrollInsets.Top = top;
            _scrollView.ScrollIndicatorInsets = scrollInsets;
            HidingNavigationBarManagerDidUpdateScrollViewInsets?.Invoke(this,null);
            
        }

        private void HandleScrollingEnded(nfloat velocity)
        {
            var minVelocity = 500;
            if (!IsViewControllerVisible() || (_navBarController.IsContracted() && velocity < minVelocity))
            {
                return;
            }

            _resistanceConsumed = 0;

            if (_currentState == HidingNavigationBarState.Contracting || _currentState == HidingNavigationBarState.Expanding || velocity > minVelocity)
            {
                var contracting = _currentState == HidingNavigationBarState.Contracting;

                if (velocity > minVelocity)
                {
                    contracting = false;
                }

                var deltaY = _navBarController.Snap(contracting);
                var tabBarShouldContract = deltaY < 0;
                _tabBarController?.Snap(tabBarShouldContract);

                var newContentOffset = _scrollView.ContentOffset;
                newContentOffset.Y -= deltaY;

                var contentInset = _scrollView.ContentInset;
                var top = contentInset.Top + deltaY;
                
                UIView.Animate(0.2, () =>
                {
                    UpdateScrollContentInsetTop(top);
                    _scrollView.ContentOffset = newContentOffset;
                });

                _previousYOffset = nfloat.NaN;
            }
        }

        public void HandelePanGesture(UIPanGestureRecognizer gesture)
        {
            switch (gesture.State)
            {
                case UIGestureRecognizerState.Began:
                    _topInset = _navBarController.View.Frame.Size.Height + _extensionController.View.Bounds.Height +
                                StatusBarHeight();
                    break;
                case UIGestureRecognizerState.Changed:
                    HandleScrolling();
                    break;

                default:
                    var velocity = gesture.VelocityInView(_scrollView).Y;
                    HandleScrollingEnded(velocity);
                    break;
            }
        }

        [Export("gestureRecognizer:shouldRecognizeSimultaneouslyWithGestureRecognizer:")]
        public bool ShouldRecognizeSimultaneously(UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer)
        {
            return true;
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing && _disposed)
            {
                _applicationWillEnterForegroundNotificationToken.Dispose();
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}