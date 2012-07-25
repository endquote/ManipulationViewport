using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Surface.Presentation;
using Microsoft.Surface.Presentation.Controls;
using Microsoft.Surface.Presentation.Manipulations;

namespace ManipulationViewportSurface
{
    /// <summary>
    /// A control whose content can be panned and zoomed within the bounds of the control. If placed within a ScatterViewItem
    /// with MaxWidth/MaxHeight properties set, it will allow scaling and panning of content within the SVI once the SVI has
    /// been scalled to its max size.
    /// </summary>
    public class ManipulationViewport : SurfaceContentControl
    {
        #region Private Fields

        /// <summary>
        /// The ContentPresenter which contains the content.
        /// </summary>
        private ContentPresenter _content;

        /// <summary>
        /// The ManipulationProcessor used for tracking manipulations.
        /// </summary>
        private Affine2DManipulationProcessor _contentManipulationProcessor;

        /// <summary>
        /// The InertiaProcessor used for internal movement when manipulations are completed.
        /// </summary>
        private Affine2DInertiaProcessor _inertiaProcessor;

        /// <summary>
        /// A second ManipulationProcessor used for tracking contacts captured to the parent ScatterViewItem.
        /// </summary>
        private Affine2DManipulationProcessor _scatterManipulationProcessor;

        /// <summary>
        /// The transform used to move the content when manipulated.
        /// </summary>
        private TranslateTransform _translate;

        /// <summary>
        /// The transform used to scale the content when manipulated.
        /// </summary>
        private ScaleTransform _scale;

        /// <summary>
        /// When the content is manipulated out of the bounds of the control, it will only move this fraction of the manipulated distance.
        /// </summary>
        private double _friction = .3;

        /// <summary>
        /// An animation which is played to move the content back into bounds.
        /// </summary>
        private Storyboard _spring;

        /// <summary>
        /// The x translation component of the spring.
        /// </summary>
        private DoubleAnimation _springTranslateX;

        /// <summary>
        /// The y translation component of the spring.
        /// </summary>
        private DoubleAnimation _springTranslateY;

        /// <summary>
        /// The x scale component of the spring.
        /// </summary>
        private DoubleAnimation _springScaleX;

        /// <summary>
        /// The y scale component of the spring.
        /// </summary>
        private DoubleAnimation _springScaleY;

        /// <summary>
        /// Whether or not to capture contacts when inside of a ScatterViewItem.
        /// </summary>
        private bool _overridingScatterViewItem;

        #endregion

        /// <summary>
        /// When overridden in a derived class, is invoked whenever application code or internal processes call <see cref="M:System.Windows.FrameworkElement.ApplyTemplate"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Portability", "CA1903:UseOnlyApiFromTargetedFramework", MessageId = "System.Windows.Media.Animation.Storyboard.#SetTarget(System.Windows.DependencyObject,System.Windows.DependencyObject)", Justification = "This will only run on 3.5SP1.")]
        public override void OnApplyTemplate()
        {
            // Set up the ContentPresenter.
            _content = GetTemplateChild("PART_Content") as ContentPresenter;
            _translate = new TranslateTransform();
            _scale = new ScaleTransform();
            _content.RenderTransform = new TransformGroup
            {
                Children = new TransformCollection { _scale, _translate }
            };
            _content.RenderTransformOrigin = new Point(.5, .5);

            // Set up the spring animation.
            _spring = new Storyboard { Duration = TimeSpan.FromMilliseconds(200), FillBehavior = FillBehavior.Stop, DecelerationRatio = .6 };
            _spring.Completed += Spring_Completed;

            _springScaleX = new DoubleAnimation { Duration = _spring.Duration, To = 1, DecelerationRatio = _spring.DecelerationRatio };
            Storyboard.SetTarget(_springScaleX, _content);
            Storyboard.SetTargetProperty(_springScaleX, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            _spring.Children.Add(_springScaleX);

            _springScaleY = new DoubleAnimation { Duration = _spring.Duration, To = 1, DecelerationRatio = _spring.DecelerationRatio };
            Storyboard.SetTarget(_springScaleY, _content);
            Storyboard.SetTargetProperty(_springScaleY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            _spring.Children.Add(_springScaleY);

            _springTranslateX = new DoubleAnimation { Duration = _spring.Duration, To = 0, DecelerationRatio = _spring.DecelerationRatio };
            Storyboard.SetTarget(_springTranslateX, _content);
            Storyboard.SetTargetProperty(_springTranslateX, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
            _spring.Children.Add(_springTranslateX);

            _springTranslateY = new DoubleAnimation { Duration = _spring.Duration, To = 0, DecelerationRatio = _spring.DecelerationRatio };
            Storyboard.SetTarget(_springTranslateY, _content);
            Storyboard.SetTargetProperty(_springTranslateY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
            _spring.Children.Add(_springTranslateY);

            _spring.Begin(this, true);

            ClipToBounds = true;

            // Set up the ManipulationProcessor.
            _contentManipulationProcessor = new Affine2DManipulationProcessor(Affine2DManipulations.Scale | Affine2DManipulations.TranslateX | Affine2DManipulations.TranslateY, this);
            _contentManipulationProcessor.Affine2DManipulationStarted += ContentManipulationProcessor_Affine2DManipulationStarted;
            _contentManipulationProcessor.Affine2DManipulationDelta += ContentManipulationProcessor_Affine2DManipulationDelta;
            _contentManipulationProcessor.Affine2DManipulationCompleted += ContentManipulationProcessor_Affine2DManipulationCompleted;

            // Set up the InertiaProcessor.
            _inertiaProcessor = new Affine2DInertiaProcessor();
            _inertiaProcessor.Affine2DInertiaDelta += ContentManipulationProcessor_Affine2DManipulationDelta;
            _inertiaProcessor.DesiredDeceleration = 96 * 1.5 * .001 * .001;
            _inertiaProcessor.ElasticMargin = new Thickness(25);

            base.OnApplyTemplate();
        }

        /// <summary>
        /// Immediately resets the scale and translation to the default position.
        /// </summary>
        internal void Reset()
        {
            if (_scale == null)
            {
                return;
            }

            _overridingScatterViewItem = false;
            _scale.ScaleX = _scale.ScaleY = 1;
            _translate.X = _translate.Y = 0;
        }

        /// <summary>
        /// Occurs when a contact over an element is placed on the Microsoft Surface screen. This method is a virtual method.
        /// </summary>
        /// <param name="e">The <strong><see cref="T:Microsoft.Surface.Presentation.ContactEventArgs"/></strong> object that contains the event data.</param>
        protected override void OnContactDown(ContactEventArgs e)
        {
            if (e != null)
            {
                if (e.Contact.IsFingerRecognized)
                {
                    e.Contact.Capture(this);
                    if (_overridingScatterViewItem)
                    {
                        e.Handled = true;
                    }
                }
            }

            base.OnContactDown(e);
        }

        /// <summary>
        /// Occurs when an element captures a contact that is touching the Microsoft Surface screen. Begin tracking that contact for manipulations.
        /// This method is a virtual method.
        /// </summary>
        /// <param name="e">The <strong><see cref="T:Microsoft.Surface.Presentation.ContactEventArgs"/></strong> object that contains the event data.</param>
        protected override void OnGotContactCapture(ContactEventArgs e)
        {
            if (e != null)
            {
                _contentManipulationProcessor.BeginTrack(e.Contact);
            }

            base.OnGotContactCapture(e);
        }

        /// <summary>
        /// Occurs when a contact capture is lost. This method is a virtual method. Stop tracking that contact for manipulations.
        /// </summary>
        /// <param name="e">The <strong><see cref="T:Microsoft.Surface.Presentation.ContactEventArgs"/></strong> object that contains the event data.</param>
        protected override void OnLostContactCapture(ContactEventArgs e)
        {
            if (e != null)
            {
                _contentManipulationProcessor.EndTrack(e.Contact);
            }

            base.OnLostContactCapture(e);
        }

        /// <summary>
        /// Handles the Affine2DManipulationStarted event of the ManipulationProcessor control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.Manipulations.Affine2DOperationStartedEventArgs"/> instance containing the event data.</param>
        private void ContentManipulationProcessor_Affine2DManipulationStarted(object sender, Affine2DOperationStartedEventArgs e)
        {
            _inertiaProcessor.End();
        }

        /// <summary>
        /// Translate and scale the content when it is manipulated.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.Manipulations.Affine2DOperationDeltaEventArgs"/> instance containing the event data.</param>
        private void ContentManipulationProcessor_Affine2DManipulationDelta(object sender, Affine2DOperationDeltaEventArgs e)
        {
            if (GetMaxScale() == 1)
            {
                return;
            }

            // The inertia processor can move the content out of bounds.
            if (sender == _inertiaProcessor)
            {
                _translate.X += e.Delta.X;
                _translate.Y += e.Delta.Y;
                _scale.ScaleX *= e.ScaleDelta;
                _scale.ScaleY *= e.ScaleDelta;
                return;
            }

            _spring.Stop(this);

            // Determine what scale to apply to the content.
            bool returnControlToScatterViewItem = false;
            double scaleDelta = 1;
            double newScale = _scale.ScaleX * e.ScaleDelta;
            if (_content.ActualWidth * newScale >= ActualWidth && newScale <= GetMaxScale())
            {
                // If the new scale is within the bounds, just use it directly.
                scaleDelta = e.ScaleDelta;
            }
            else if (e.ScaleDelta > 1)
            {
                // Apply friction to an increasing scale.
                scaleDelta = 1 + ((e.ScaleDelta - 1) * _friction);
            }
            else if (e.ScaleDelta < 1)
            {
                // Apply friction to a decreasing scale.
                scaleDelta = 1 - ((1 - e.ScaleDelta) * _friction);
                returnControlToScatterViewItem = _scale.ScaleX <= .95;
            }

            _scale.ScaleX *= scaleDelta;
            _scale.ScaleY *= scaleDelta;

            if (returnControlToScatterViewItem)
            {
                // If the content is scaled down to less than the default size, return control back to the ScatterViewItem.
                if (ScatterViewItem != null)
                {
                    ContactsCaptured.ToList().ForEach(c => c.Capture(ScatterViewItem));
                    _overridingScatterViewItem = false;
                }
            }

            // Move the content.
            _translate.X += e.Delta.X;
            _translate.Y += e.Delta.Y;

            // Get the new bounds of the image.
            Rect bounds = GetContentBounds();

            if (bounds.TopLeft.X > 0 || bounds.BottomRight.X < ActualWidth)
            {
                // Apply friction if the content is out of bounds on the x-axis.
                _translate.X -= e.Delta.X;
                _translate.X += e.Delta.X * _friction;
            }

            if (bounds.TopLeft.Y > 0 || bounds.BottomRight.Y < ActualHeight)
            {
                // Apply friction if the content is out of bounds on the y-axis.
                _translate.Y -= e.Delta.Y;
                _translate.Y += e.Delta.Y * _friction;
            }
        }

        /// <summary>
        /// When the manipulation is completed, either spring the content back into bounds, or begin inertia.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.Manipulations.Affine2DOperationCompletedEventArgs"/> instance containing the event data.</param>
        private void ContentManipulationProcessor_Affine2DManipulationCompleted(object sender, Affine2DOperationCompletedEventArgs e)
        {
            if (Spring())
            {
                return;
            }

            Rect bounds = GetContentBounds();
            _inertiaProcessor.InitialOrigin = new Point();
            _inertiaProcessor.InitialVelocity = e.Velocity;
            _inertiaProcessor.Bounds = new Thickness(ActualWidth - bounds.BottomRight.X, ActualHeight - bounds.BottomRight.Y, -bounds.TopLeft.X, -bounds.TopLeft.Y);
            _inertiaProcessor.Begin();
        }

        /// <summary>
        /// Spring the content back into bounds if needed.
        /// </summary>
        /// <returns>A value indicating whether the content needed to be brought into bounds.</returns>
        private bool Spring()
        {
            // Reset the spring animation.
            _springScaleX.To = _springScaleY.To = _scale.ScaleX;
            _springTranslateX.To = _translate.X;
            _springTranslateY.To = _translate.Y;

            Rect bounds = GetContentBounds();
            double maxScale = GetMaxScale();
            bool doSpring = false;
            if (_content.ActualWidth * _scale.ScaleX <= ActualWidth)
            {
                // The content has been scaled too small, so spring back to the default size.
                _springScaleX.To = _springScaleY.To = 1;
                _springTranslateX.To = _springTranslateY.To = 0;
                doSpring = true;
            }
            else if (_scale.ScaleX > maxScale)
            {
                // The content has been scaled too large, so spring back to the max scale.
                _springScaleX.To = _springScaleY.To = maxScale;
                doSpring = true;
            }
            else
            {
                if (bounds.TopLeft.X > 0)
                {
                    // The content is too far to the right.
                    _springTranslateX.To = _translate.X - bounds.TopLeft.X;
                    doSpring = true;
                }
                else if (bounds.BottomRight.X < ActualWidth)
                {
                    // The content is too far to the left.
                    _springTranslateX.To = _translate.X + ActualWidth - bounds.BottomRight.X;
                    doSpring = true;
                }

                if (bounds.TopLeft.Y > 0)
                {
                    // The content is too far up.
                    _springTranslateY.To = _translate.Y - bounds.TopLeft.Y;
                    doSpring = true;
                }
                else if (bounds.BottomRight.Y < ActualHeight)
                {
                    // The content is too far down.
                    _springTranslateY.To = _translate.Y + ActualHeight - bounds.BottomRight.Y;
                    doSpring = true;
                }
            }

            if (doSpring)
            {
                _spring.Begin(this, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// When the spring is completed, replace the current transform values with the springed values. Spring again if needed.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void Spring_Completed(object sender, EventArgs e)
        {
            _scale.ScaleX = (double)_springScaleX.To;
            _scale.ScaleY = (double)_springScaleY.To;
            _translate.X = (double)_springTranslateX.To;
            _translate.Y = (double)_springTranslateY.To;

            Spring();
        }

        /// <summary>
        /// Get the current visual bounds of the content.
        /// </summary>
        /// <returns>The current visual bounds of the content</returns>
        private Rect GetContentBounds()
        {
            Rect bounds = VisualTreeHelper.GetDescendantBounds(_content);
            GeneralTransform generalTransform = _content.TransformToVisual(this);
            Point topLeft = generalTransform.Transform(bounds.TopLeft);
            Point bottomRight = generalTransform.Transform(bounds.BottomRight);
            return new Rect(topLeft, bottomRight);
        }

        /// <summary>
        /// If the content is an Image, don't allow scaling beyond the natural pixel dimensions of the image.
        /// </summary>
        /// <returns>The max scale, based on the pixel dimensions of any nested image.</returns>
        private double GetMaxScale()
        {
            if (_content == null)
            {
                return MaximumScaleFactor;
            }

            Image image = _content.FindVisualChild<Image>();
            if (image == null)
            {
                return MaximumScaleFactor;
            }

            BitmapSource source = image.Source as BitmapSource;
            if (source == null)
            {
                return MaximumScaleFactor;
            }

            return Math.Max(1, MaximumScaleFactor * Math.Min(source.PixelWidth / image.ActualWidth, source.PixelHeight / image.ActualHeight));
        }

        #region MaximumScaleFactor

        /// <summary>
        /// Gets or sets the maximum scale factor.
        /// </summary>
        /// <value>The maximum scale factor.</value>
        public double MaximumScaleFactor
        {
            get { return (double)GetValue(MaximumScaleFactorProperty); }
            set { SetValue(MaximumScaleFactorProperty, value); }
        }

        /// <summary>
        /// The identifier for the MaximumScaleFactor dependency property.
        /// </summary>
        public static readonly DependencyProperty MaximumScaleFactorProperty = DependencyProperty.Register("MaximumScaleFactor", typeof(double), typeof(ManipulationViewport), new PropertyMetadata(double.PositiveInfinity));

        #endregion

        #region ScatterViewItem

        /// <summary>
        /// Gets or sets the ScatterViewItem in which this control is contained.
        /// </summary>
        /// <value>The scatter view item.</value>
        public ScatterViewItem ScatterViewItem
        {
            get { return (ScatterViewItem)GetValue(ScatterViewItemProperty); }
            set { SetValue(ScatterViewItemProperty, value); }
        }

        /// <summary>
        /// The identifier for the ScatterViewItem dependency property.
        /// </summary>
        public static readonly DependencyProperty ScatterViewItemProperty = DependencyProperty.Register(
            "ScatterViewItem",
            typeof(ScatterViewItem),
            typeof(ManipulationViewport),
            new PropertyMetadata(null, (sender, e) => (sender as ManipulationViewport).UpdateScatterViewItem(e.OldValue as ScatterViewItem)));

        /// <summary>
        /// Fired when ScatterViewItem is changed.
        /// </summary>
        /// <param name="oldValue">The old value.</param>
        private void UpdateScatterViewItem(ScatterViewItem oldValue)
        {
            if (oldValue != null)
            {
                // Clean up the old SVI.
                ScatterViewItem.PreviewContactDown -= ScatterViewItem_PreviewContactDown;
                ScatterViewItem.PreviewContactUp -= ScatterViewItem_PreviewContactUp;
                ScatterViewItem.ScatterManipulationDelta -= ScatterViewItem_ScatterManipulationDelta;
                _scatterManipulationProcessor.Affine2DManipulationDelta -= ScatterManipulationProcessor_Affine2DManipulationDelta;
                _scatterManipulationProcessor = null;
            }

            if (ScatterViewItem != null)
            {
                // Set up the new SVI.
                ScatterViewItem.PreviewContactDown += ScatterViewItem_PreviewContactDown;
                ScatterViewItem.PreviewContactUp += ScatterViewItem_PreviewContactUp;
                ScatterViewItem.ScatterManipulationDelta += ScatterViewItem_ScatterManipulationDelta;
                _scatterManipulationProcessor = new Affine2DManipulationProcessor(Affine2DManipulations.Scale, ScatterViewItem);
                _scatterManipulationProcessor.Affine2DManipulationDelta += ScatterManipulationProcessor_Affine2DManipulationDelta;
            }
        }

        /// <summary>
        /// Handles the ScatterManipulationDelta event of the ScatterViewItem control. If the SVI is scaled down, restore the content to its original scale.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.Controls.ScatterManipulationDeltaEventArgs"/> instance containing the event data.</param>
        private void ScatterViewItem_ScatterManipulationDelta(object sender, ScatterManipulationDeltaEventArgs e)
        {
            if (e == null || _springScaleX == null || _springScaleY == null || _springTranslateX == null || _springTranslateY == null)
            {
                return;
            }

            if (e.ScaleFactor < 1 && _overridingScatterViewItem)
            {
                // If the SVI is scaled down and this control is stealing contacts, restore the size of the content and stop taking over.
                _springScaleX.To = _springScaleY.To = 1;
                _springTranslateX.To = _springTranslateY.To = 0;
                _spring.Begin(this, true);
                _overridingScatterViewItem = false;
            }
        }

        /// <summary>
        /// Handles the PreviewContactDown event of the ScatterViewItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.ContactEventArgs"/> instance containing the event data.</param>
        private void ScatterViewItem_PreviewContactDown(object sender, ContactEventArgs e)
        {
            _scatterManipulationProcessor.BeginTrack(e.Contact);
        }

        /// <summary>
        /// Handles the PreviewContactUp event of the ScatterViewItem control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.ContactEventArgs"/> instance containing the event data.</param>
        private void ScatterViewItem_PreviewContactUp(object sender, ContactEventArgs e)
        {
            _scatterManipulationProcessor.EndTrack(e.Contact);
        }

        /// <summary>
        /// Handles the Affine2DManipulationDelta event of the ScatterManipulationProcessor control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Microsoft.Surface.Presentation.Manipulations.Affine2DOperationDeltaEventArgs"/> instance containing the event data.</param>
        private void ScatterManipulationProcessor_Affine2DManipulationDelta(object sender, Affine2DOperationDeltaEventArgs e)
        {
            if (!_overridingScatterViewItem && e.ScaleDelta >= 1 &&
                GetMaxScale() > 1 &&
                (Math.Abs(ScatterViewItem.ActualWidth - ScatterViewItem.MaxWidth) <= .1 || Math.Abs(ScatterViewItem.ActualHeight - ScatterViewItem.MaxHeight) <= .1))
            {
                // If the SVI is scalled up to its maximum, steal its contacts and begin scaling the content.
                _overridingScatterViewItem = true;
                ScatterViewItem.ContactsCaptured.ToList().ForEach(c => c.Capture(this));
            }
        }

        #endregion
    }
}
