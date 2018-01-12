﻿using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BrainCommon;
using BrainNetwork.BrainDeviceProtocol;
using DataAccess;
using SciChart.Charting.Common.Helpers;
using SciChart_50ChannelEEG;

namespace SciChart.Examples.Examples.CreateRealtimeChart.EEGChannelsDemo
{
    public partial class EEGExampleViewModel
    {
        private BrainDevState _currentState;
        private int _pakNum;
        private Dispatcher _uithread;
        private DevCommandSender _devCtl;
        private ConcurrentQueue<double[]> cache = new ConcurrentQueue<double[]>();
        private FileResource currentFileResource;

        private readonly ActionCommand _impedanceCommand;
        private ImpedanceViewWin _impedanceView;
        public ICommand ImpedanceCommand
        {
            get { return _impedanceCommand; }
        }

        private void ShowImpedanceView()
        {
            if (_devCtl == null)
            {
                _impedanceView?.Activate();
                return;
            }
            if (_impedanceView == null)
            {
                _impedanceView = new ImpedanceViewWin(_currentState);
                _impedanceView.Closing += OnImpedanceViewClosing;
            }
            else
            {
                _impedanceView.Activate();
            }
            _devCtl.TestMultiImpedance(_currentState.ChannelCount);
            _impedanceView.Show();
        }

        private void OnImpedanceViewClosing(object sender, CancelEventArgs e)
        {
            _impedanceView = null;
        }

        public EEGExampleViewModel()
        {
            _startCommand = new ActionCommand(StartAsync, () => !IsRunning);
            _stopCommand = new ActionCommand(StopDevCmd, () => IsRunning);
            _resetCommand = new ActionCommand(ResetDevCmd, () => !IsRunning && !IsReset);
            _impedanceCommand = new ActionCommand(ShowImpedanceView, () => _impedanceView != null || _devCtl != null);

            _uithread = Dispatcher.CurrentDispatcher;
            _currentState = default(BrainDevState);
            AppDomain.CurrentDomain.ProcessExit += ProcessExit;
            ClientConfig.GetConfig();
        }

        private void ProcessExit(object sender, EventArgs e)
        {
            BrainDeviceManager.DisConnect();
            ClientConfig.OnAppExit();
        }

        private void CheckUpdate(object sender, ElapsedEventArgs e)
        {
            lock (_syncRoot)
            {
                while (cache.TryDequeue(out var voltageArr))
                {
                    UpdateChannelBuffer(voltageArr);
                    BrainDeviceManager.BufMgr.ReturnBuffer(voltageArr);
                }

                if (!IsRunning || _channelViewModels == null) return;
                _uithread.InvokeAsync(FlushAllChannels);
            }
        }

        private void FlushAllChannels()
        {
            for (var i = 0; i < _channelViewModels.Count; i++)
            {
                var channel = _channelViewModels[i];
                channel.FlushBuf();
                _currentSize = channel.ChannelDataSeries.Count;
            }
        }

        private void UpdateChannelBuffer(double[] voltageArr)
        {
            if (_channelViewModels == null) return;
            var passTimes = BrainDevState.PassTimeMs(_currentState.SampleRate, _pakNum);
            _pakNum++;
            for (var i = 0; i < _channelViewModels.Count; i++)
            {
                var channel = _channelViewModels[i];
                channel.BufferChannelData(passTimes, voltageArr[i]);
            }
        }

        private void Disconnect()
        {
            BrainDeviceManager.DisConnect();
            _running = false;
            _devCtl = null;
        }

        private void StartAsync()
        {
            Task.Factory.StartNew(() =>
            {
                StartDevCmd().Wait();
            }).ContinueWith(t => { UpdateRuningStates(); },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task StartDevCmd()
        {
            if (_running) return;

            _running = true;
            _isReset = false;
            _pakNum = 0;
            if (_devCtl != null)
            {
                if (!await StartSampleAsync(_devCtl))
                {
                    Disconnect();
                    return;
                }
            }
            else
            {
                var sender = await ConnectDevAsync();
                if (sender == null || !await StartSampleAsync(sender))
                {
                    Disconnect();
                    return;
                }
                _devCtl = sender;
            }
        }

        private void UpdateRuningStates()
        {
            IsReset = _isReset;
            IsRunning = _running;
            _impedanceCommand.RaiseCanExecuteChanged();
            if (_running)
            {
                _timer = new Timer(_timerInterval);
                _timer.Elapsed += CheckUpdate;
                _timer.AutoReset = true;
                _timer.Start();
                //PauseChart(false);
            }
            else
            {
                _timer?.Stop();
            }
        }

        private async void StopDevCmd()
        {
            if (IsRunning)
            {
                IsRunning = false;
                _timer?.Stop();
                if (_devCtl != null)
                    await _devCtl.Stop();
                //PauseChart(true);
            }
        }

        private void PauseChart(bool isCont)
        {
            if (_channelViewModels == null) return;
            for (var i = 0; i < _channelViewModels.Count; i++)
            {
                var channel = _channelViewModels[i];
                if (isCont)
                    channel.PauseX();
                else
                    channel.Resume();
            }
        }

        private void ResetDevCmd()
        {
            IsReset = true;
            StopDevCmd();
            Disconnect();
            currentFileResource = null;
            ResetChannelParts();
        }

        private void ResetChannelParts()
        {
            for (var i = 0; i < _channelViewModels.Count; i++)
            {
                var channel = _channelViewModels[i];
                channel.Reset();
            }
        }

        private void RefreshChannelParts()
        {
            _impedanceCommand.RaiseCanExecuteChanged();

            if (ChannelViewModels != null && ChannelViewModels.Count == ChannelCount)
            {
                PauseChart(!_currentState.IsStart);
                return;
            }
            CreateChannelPart();
        }

        private void CreateChannelPart()
        {
            var colorsCount = _colors.Count;
            var count = BrainDevState.SampleCountPer20ms(_currentState.SampleRate);
            ChannelViewModels = new ObservableCollection<EEGChannelViewModel>();
            for (int i = 0; i < ChannelCount; i++)
            {
                var channelViewModel =
                    new EEGChannelViewModel(Size, _colors[i % colorsCount], count) { ChannelName = "Channel " + (i + 1) };

                ChannelViewModels.Add(channelViewModel);
            }
        }

        private async Task<DevCommandSender> ConnectDevAsync()
        {
            try
            {
                BrainDeviceManager.Init();
                var cfg = ClientConfig.GetConfig();
                var sender = await BrainDeviceManager.Connnect(cfg.Ip, cfg.Port);

                //保证设备参数正常才继续跑逻辑
                BrainDeviceManager.BrainDeviceState.Subscribe(ss =>
                {
                    _currentState = ss;
                    ChannelCount = _currentState.ChannelCount;
                    _uithread.InvokeAsync(RefreshChannelParts);
                    AppLogger.Debug($"Brain Device State Changed Detected: {ss}");
                }, () => { AppLogger.Debug("device stop detected"); });
                BrainDeviceManager.SampleDataStream.Subscribe(tuple =>
                {
                    var (order, datas, arr) = tuple;
                    var buf = datas.Array;
                    if (buf != null)
                    {
                        var cfglocal = ClientConfig.GetConfig();
                        var startIdx = datas.Offset;
                        var voltageArr = BrainDeviceManager.BufMgr.TakeDoubleBuf(datas.Count);
                        //var voltageArr = new double[datas.Count];
                        for (var i = 0; i < datas.Count; i++)
                        {
                            voltageArr[i] =
                                BitDataConverter.Calculatevoltage(buf[startIdx + i], cfglocal.ReferenceVoltage, _currentState.Gain);
                        }
                        cache.Enqueue(voltageArr);
                    }
                }, () =>
                {
                    _devCtl = null;
                    _uithread.InvokeAsync(ResetDevCmd);
                    AppLogger.Debug("device sampling stream closed detected");
                });

                var cmdResult = await sender.QueryParam();
                AppLogger.Debug("QueryParam result:" + cmdResult);
                if (cmdResult != CommandError.Success)
                {
                    AppLogger.Error("Failed to QueryParam, stop");
                    BrainDeviceManager.DisConnect();
                    return null;
                }
                return sender;
            }
            catch (Exception)
            {
                return null;
            }
            /*
            cmdResult = await sender.SetSampleRate(SampleRateEnum.SPS_2k);
            AppLogger.Debug("SetSampleRate result:"+cmdResult);
            
            cmdResult = await sender.QueryParam();
            AppLogger.Debug("QueryParam result:"+cmdResult);
            */
        }

        private async Task<bool> StartSampleAsync(DevCommandSender sender)
        {
            FileResource fs = null;
            try
            {
                if (currentFileResource == null)
                {
                    var cfg = ClientConfig.GetConfig();
                    fs = new FileResource(_currentState, cfg.DeviceId, 1, BrainDeviceManager.BufMgr);
                    fs.StartRecord(BrainDeviceManager.SampleDataStream);
                }
                var cmdResult = await sender.Start();
                if (cmdResult != CommandError.Success)
                {
                    AppLogger.Error("Failed to start sampler");
                    fs?.Dispose();
                    return false;
                }
                if (currentFileResource == null)
                    currentFileResource = fs;
                return true;
            }
            catch (Exception)
            {
                fs?.Dispose();
                return false;
            }
        }
    }

    public partial class EEGExampleView
    {

        private void ChannelListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            //e.VerticalOffset: In the current view, index of the element which is first in the list view
            //e.ViewportHeight: Number of list view items shown in the current view.
            var scrollViewer = sender as FrameworkElement;
            var eegExampleViewModel = (DataContext as EEGExampleViewModel);
            try
            {
                var count = eegExampleViewModel.ChannelViewModels.Count;
                int lastIdx = (int) (e.VerticalOffset + e.ViewportHeight);
                lastIdx = lastIdx < count ? lastIdx : count - 1;
                var vm = ChannelListBox.Items[lastIdx] as EEGChannelViewModel;
                var tmp = vm?.ChannelDataSeries?.ParentSurface?.RootGrid;
                var tmp2 = tmp as FrameworkElement;
                var lastVisible = tmp2 == null ? false : IsFullyOrPartiallyVisible(tmp2, scrollViewer);
                if (!lastVisible) lastIdx--;
                AppLogger.Debug($"ChannelListBox_ScrollChanged: {scrollViewer}, {e.VerticalOffset},{e.ViewportHeight},{lastVisible}");
                for (int i = 0;i< ChannelListBox.Items.Count; i++)
                {
                    var isvisible = e.VerticalOffset <= i && i <= lastIdx;
                    var viewmodel = ChannelListBox.Items[i] as EEGChannelViewModel;
                    viewmodel.OptVisible(isvisible);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex.Message);
            }
        }

        protected bool IsFullyOrPartiallyVisible(FrameworkElement child, FrameworkElement scrollViewer)
        {
            var childTransform = child.TransformToAncestor(scrollViewer);
            var childRectangle = childTransform.TransformBounds(new Rect(new Point(0, 0), child.RenderSize));
            var ownerRectangle = new Rect(new Point(0, 0), scrollViewer.RenderSize);
            return ownerRectangle.IntersectsWith(childRectangle);
        }
    }
}