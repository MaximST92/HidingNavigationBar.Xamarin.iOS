using System;
using System.Collections.Generic;
using CoreGraphics;
using UIKit;

namespace HidingNavigationBar.Xamarin.iOS
{
    public class HidingViewController
    {
        public const float Epsilon = 1.192092896e-07F;
        public HidingViewController Child { get; set; }

        private List<UIView> _navSubviews;
        internal UIView View;

        public bool AlphaFadeEnable { get; set; }
        public bool ContractsUpwards { get; set; }

        public Func<UIView,CGPoint> ExpandedCenter { get; set; }

        public HidingViewController(UIView view)
        {
            ContractsUpwards = true;
            View = view;
        }

        public HidingViewController()
        {
            ContractsUpwards = true;
            View = new UIView(CGRect.Empty);
            View.BackgroundColor = UIColor.Clear;
            View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleBottomMargin;
        }


        public CGPoint ExpandedCenterValue()
        {
            if (ExpandedCenter != null )
            {
                return ExpandedCenter.Invoke(View);
            }
            return CGPoint.Empty;
        }

        public float ContractionAmountValue()
        {
            return (float)View.Bounds.Height;
        }

        public CGPoint ContractedCenterValue()
        {
            if (ContractsUpwards)
            {
                return new CGPoint(ExpandedCenterValue().X,ExpandedCenterValue().Y - ContractionAmountValue());
            }
                return new CGPoint(ExpandedCenterValue().X,ExpandedCenterValue().Y+ ContractionAmountValue());
        }

        public bool IsContracted()
        {
            return Math.Abs(View.Center.Y - ContractedCenterValue().Y) < Epsilon;
        }
        
        public bool IsExpanded()
        {
            return Math.Abs(View.Center.Y - ExpandedCenterValue().Y) < Epsilon;
        }

        public nfloat TotalHeight()
        {
            var height = ExpandedCenterValue().Y - ContractedCenterValue().Y;
            if (Child !=null)
            {
                return Child.TotalHeight() + height;
            }
            return height;
        }

        public void SetAlphaFadeEnabled(bool alphaEnabled)
        {
            AlphaFadeEnable = alphaEnabled;
            if (!alphaEnabled)
            {
                UpdateSubviewsToAlpha(1);
            }
        }

        public nfloat UpdateYOffset(nfloat delta)
        {
            var deltaY = delta;
            if (Child!= null && deltaY < 0)
            {
                deltaY = Child.UpdateYOffset(deltaY);
                Child.View.Hidden = (deltaY) < 0;
            }

            var newYOffset = View.Center.Y + deltaY;
            var newYCenter = (float)Math.Max(Math.Min(ExpandedCenterValue().Y, newYOffset), ContractedCenterValue().Y);
            if (!ContractsUpwards)
            {
                newYOffset = View.Center.Y - deltaY;
                newYCenter =(float) Math.Min(Math.Max(ExpandedCenterValue().Y, newYOffset), ContractedCenterValue().Y);
            }
            
            View.Center = new CGPoint(View.Center.X,newYCenter);

            if (AlphaFadeEnable)
            {
                var newAlpha = 1f - (ExpandedCenterValue().Y - View.Center.Y) * 2 / ContractionAmountValue();
                newAlpha = Math.Min(Math.Max(Epsilon, (float) newAlpha), 1f);
                UpdateSubviewsToAlpha((float)newAlpha);
            }

            var residual = newYOffset - newYCenter;
            if (Child != null && deltaY > 0 && residual>0)
            {
                residual = Child.UpdateYOffset((float)residual);
                Child.View.Hidden = residual - (newYOffset - newYCenter) > 0;
            }

            return residual;
        }

        public nfloat Snap(bool contract, Action completion = null)
        {
            nfloat deltaY = 0f;
            UIView.Animate(0.2, () =>
            {
                if (Child != null)
                {
                    if (contract && Child.IsContracted())
                    {
                        deltaY = Contract();
                    }
                    else
                    {
                        deltaY = Expand();
                    }
                }
                else
                {
                    if (contract)
                    {
                        deltaY = Contract();
                    }
                    else
                    {
                        deltaY = Expand();
                    }
                }
            }, () => completion?.Invoke());

            return deltaY;
        }

        public nfloat Expand()
        {
            View.Hidden = false;

            if (AlphaFadeEnable)
            {
                UpdateSubviewsToAlpha(1);
                _navSubviews = null;
            }

            var amountToMove = ExpandedCenterValue().Y - View.Center.Y;

            View.Center = ExpandedCenterValue();

            if (Child != null)
            {
                amountToMove += Child.Expand();
            }

            return amountToMove;
        }

        public nfloat Contract()
        {
            if (AlphaFadeEnable)
            {
                UpdateSubviewsToAlpha(0);
            }

            var amountToMove = ContractedCenterValue().Y - View.Center.Y;

            View.Center = ContractedCenterValue();

            return amountToMove;
        }

        private void UpdateSubviewsToAlpha(float alpha)
        {
            if (_navSubviews == null)
            {
                _navSubviews = new List<UIView>();

                foreach (var subview in View.Subviews)
                {
                    var isBackgroundView = Equals(subview, View.Subviews[0]);
                    var isViewHidden = subview.Hidden || Math.Abs(subview.Alpha) < Epsilon;

                    if (!isBackgroundView && !isViewHidden)
                    {
                       _navSubviews.Add(subview);
                    }
                }
            }

            foreach (var view in _navSubviews)
            {
                view.Alpha = alpha;
            }
        }
    }
}