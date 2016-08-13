using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageCropTool
{
    /// <summary>
    ///     MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, CropImageTask> _cropTaskDic;
        private readonly Dictionary<string, BitmapImage> _imgDic;
        private readonly List<int> TileSizes = new List<int> { 16, 32, 64, 128, 256, 512 };
        private int _tileSize = 256;
        private Thread timeThread;

        public MainWindow()
        {
            InitializeComponent();

            _imgDic = new Dictionary<string, BitmapImage>();
            TileSizeComboBox.ItemsSource = TileSizes;
            TileSizeComboBox.SelectedItem = 256;

            _cropTaskDic = new Dictionary<string, CropImageTask>();
            timeThread = new Thread(TimeUpdateThread);
            timeThread.Start();
        }

        private void lstIamge_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (string file in files)
            {
                if (_imgDic.ContainsKey(file))
                {
                    MessageBox.Show("已存在此文件:" + file);
                    continue;
                }

                try
                {
                    var image = new BitmapImage(new Uri(file));
                    imgShow.Source = image;
                    lstImage.Items.Add(file);
                    _imgDic.Add(file, image);

                    if (lstImage.SelectedItem == null)
                    {
                        lstImage.SelectedItem = file;
                    }
                }
                catch (Exception exception)
                {
                    MessageBox.Show("导入了非图像文件:" + exception.Message);
                }
            }
        }

        private void lstImage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstImage.SelectedIndex > -1)
            {
                string file = lstImage.SelectedItem.ToString();
                BitmapImage image = null;
                if (_imgDic.TryGetValue(file, out image))
                {
                    imgShow.Source = image;
                }
                else
                {
                    imgShow.Source = null;
                }
            }
        }

        private void LstImage_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (lstImage.SelectedItem == null) return;

            if (e.Key == Key.Delete)
            {
                string file = lstImage.SelectedItem.ToString();

                _imgDic.Remove(file);
                lstImage.Items.Remove(file);

                lstImage.SelectedItem = null;
                imgShow.Source = null;
            }
        }

        private void OnClickClearBtn(object sender, RoutedEventArgs e)
        {
            if (lstImage.Items.Count == 0 || _imgDic.Count == 0) return;

            lstImage.Items.Clear();
            _imgDic.Clear();
        }

        private void OnClickCropAll(object sender, RoutedEventArgs e)
        {
            if (lstImage.Items.Count == 0 || _imgDic.Count == 0) return;

            foreach (string file in lstImage.Items)
            {
                CropImage(file);
            }
        }

        private void OnClickCropSelection(object sender, RoutedEventArgs e)
        {
            if (lstImage.SelectedItem == null) return;

            CropImage(lstImage.SelectedItem.ToString());
        }

        private void OnClickOutputDirBtn(object sender, RoutedEventArgs e)
        {
            if (lstImage.SelectedItem == null) return;

            OpenOutputDir(lstImage.SelectedItem.ToString());
        }

        private void OpenOutputDir(string imgPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(imgPath);
            var outputDir = Path.Combine(Path.GetDirectoryName(imgPath), fileName);
            if (Directory.Exists(outputDir))
            {
                Process.Start(outputDir);
            }
            else
            {
                MessageBox.Show("不存在输出目录:" + outputDir);
            }
        }

        private void CropImage(string imgPath)
        {
            if (_cropTaskDic.ContainsKey(imgPath))
            {
                Console.WriteLine("Has processing image:" + imgPath);
                return;
            }

            BitmapImage image = null;
            if (!_imgDic.TryGetValue(imgPath, out image)) return;

            int imageWidth = image.PixelWidth;
            int imageHeight = image.PixelHeight;
            if (imageWidth % _tileSize != 0 || imageHeight % _tileSize != 0)
            {
                MessageBox.Show(string.Format("当前裁剪图片尺寸不是<{0}>的倍数", _tileSize));
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(imgPath);
            var outputDir = Path.Combine(Path.GetDirectoryName(imgPath), fileName);
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }

            Directory.CreateDirectory(outputDir);

            CropImageTask task = new CropImageTask
            {
                outputDir = outputDir,
                imgPath = imgPath,
                wCount = imageWidth / _tileSize,
                hCount = imageHeight / _tileSize,
            };

            var thread = new Thread(CropImageThread) { IsBackground = true };
            task.thread = thread;
            thread.Start(task);
            _cropTaskDic.Add(imgPath, task);
        }

        private void CropImageThread(object obj)
        {
            var task = obj as CropImageTask;
            string imageName = Path.GetFileNameWithoutExtension(task.imgPath);
            for (int w = 0; w < task.wCount; w++)
            {
                for (int h = 0; h < task.hCount; h++)
                {
                    var subTask = new CropImageSubTask
                    {
                        mainTask = task,
                        x = _tileSize * w,
                        y = (task.hCount - (h + 1)) * _tileSize,
                        outputFile = Path.Combine(task.outputDir, string.Format("{0}_{1}_{2}.png", imageName, w, h))
                    };

                    ThreadPool.QueueUserWorkItem(CropImageSubThread, subTask);
                    Thread.Sleep(100);
                }
            }
        }

        private void CropImageSubThread(object obj)
        {
            var subTask = obj as CropImageSubTask;
            var pInfo = new ProcessStartInfo
            {
                FileName = "nconvert.exe",
                Arguments =
                            string.Format("-out png -o {0} -crop {1} {2} {3} {4} {5}", subTask.outputFile, subTask.x, subTask.y, _tileSize, _tileSize,
                                subTask.mainTask.imgPath),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(pInfo);
            string outputLog = process.StandardOutput.ReadToEnd();
            string errorLog = process.StandardError.ReadToEnd();
            while (!process.HasExited)
            {
                Thread.Sleep(100);
            }

            int exitCode = process.ExitCode;
            process.Close();

            subTask.mainTask.finishCount++;
            if (exitCode != 0)
            {
                MessageBox.Show("CropTile <" + subTask.outputFile + "> failed. exit code = " + exitCode + "\nerror:" + errorLog);
            }
        }

        private void TimeUpdateThread()
        {
            while (true)
            {
                cropProgressBar.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(UpdateCropProgress));
                Thread.Sleep(500);
            }
        }

        private void UpdateCropProgress()
        {
            if (_cropTaskDic.Count > 0)
            {
                lstCropProgress.Items.Clear();
                int allFinish = 0;
                int allTotal = 0;
                var sb = new StringBuilder();
                var removeList = new List<string>();
                foreach (var task in _cropTaskDic.Values)
                {
                    int total = task.hCount * task.wCount;
                    lstCropProgress.Items.Add(string.Format("{0},已完成{1}/{2}", task.imgPath, task.finishCount, total));
                    allTotal += total;
                    allFinish += task.finishCount;
                    if (task.finishCount >= total)
                    {
                        removeList.Add(task.imgPath);
                    }
                }

                cropProgressBar.Value = (double)allFinish / allTotal;

                foreach (string key in removeList)
                {
                    _cropTaskDic.Remove(key);
                }
            }
            else
            {
                cropProgressBar.Value = 0;
            }
        }

        private void OnChangeTileSize(object sender, SelectionChangedEventArgs e)
        {
            _tileSize = Convert.ToInt32(TileSizeComboBox.SelectedValue);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (timeThread != null)
                timeThread.Abort();

            if (_cropTaskDic != null)
            {
                foreach (var task in _cropTaskDic.Values)
                {
                    if (task != null && task.thread != null)
                        task.thread.Abort();
                }
            }

            Application.Current.Shutdown();
        }

        #region Nested type: CropInfo

        public class CropImageTask
        {
            public string outputDir;
            public string imgPath;
            public int finishCount;
            public int wCount;
            public int hCount;
            public Thread thread;
        }


        public class CropImageSubTask
        {
            public CropImageTask mainTask;
            public string outputFile;
            public int x;
            public int y;
        }
        #endregion
    }
}