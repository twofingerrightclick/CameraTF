﻿using Emgu.TF.Lite;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TailwindTraders.Mobile.Features.Scanning.AR;

namespace TailwindTraders.Mobile.Features.Scanning
{
    public class TensorflowLiteService
    {
        public const int ModelInputSize = 300;
        public const float MinScore = 0.6f;

        private const int LabelOffset = 1;

        private byte[] quantizedColors;
        private bool initialized = false;
        private string[] labels = null;
        private FlatBufferModel model;

        private bool useNumThreads = true;

        public void Initialize(Stream modelData, Stream labelData)
        {
            if (initialized)
            {
                return;
            }

            using (var ms = new MemoryStream())
            {
                labelData.CopyTo(ms);

                var labelContent = Encoding.Default.GetString(ms.ToArray());

                labels = labelContent
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();
            }

            using (var ms = new MemoryStream())
            {
                modelData.CopyTo(ms);

                model = new FlatBufferModel(ms.ToArray());
            }

            if (!model.CheckModelIdentifier())
            {
                throw new Exception("Model identifier check failed");
            }

            quantizedColors = new byte[ModelInputSize * ModelInputSize * 3];

            initialized = true;
        }

        public void Recognize(int[] colors)
        {
            if (!initialized)
            {
                throw new Exception("Initialize TensorflowLiteService first");
            }

            using (var op = new BuildinOpResolver())
            {
                using (var interpreter = new Interpreter(model, op))
                {
                    InvokeInterpreter(colors, interpreter);
                }
            }
        }

        private void InvokeInterpreter(int[] colors, Interpreter interpreter)
        {
            if (useNumThreads)
            {
                interpreter.SetNumThreads(Environment.ProcessorCount);
            }

            var allocateTensorStatus = interpreter.AllocateTensors();
            if (allocateTensorStatus == Status.Error)
            {
                throw new Exception("Failed to allocate tensor");
            }

            var input = interpreter.GetInput();
            using (var inputTensor = interpreter.GetTensor(input[0]))
            {
                CopyColorsToTensor(inputTensor.DataPointer, colors);

                var watchInvoke = Stopwatch.StartNew();
                interpreter.Invoke();
                watchInvoke.Stop();

                Console.WriteLine($"InterpreterInvoke: {watchInvoke.ElapsedMilliseconds}ms");
            }

            var output = interpreter.GetOutput();
            var outputIndex = output[0];

            var outputTensors = new Tensor[output.Length];
            for (var i = 0; i < output.Length; i++)
            {
                outputTensors[i] = interpreter.GetTensor(outputIndex + i);
            }

            var detection_boxes_out = outputTensors[0].GetData() as float[];
            var detection_classes_out = outputTensors[1].GetData() as float[];
            var detection_scores_out = outputTensors[2].GetData() as float[];
            var num_detections_out = outputTensors[3].GetData() as float[];

            var numDetections = num_detections_out[0];

            LogDetectionResults(detection_classes_out, detection_scores_out, detection_boxes_out, (int)numDetections);
        }

        private void CopyColorsToTensor(IntPtr dest, int[] colors)
        {
            for (var i = 0; i < colors.Length; ++i)
            {
                var val = colors[i];

                //// AA RR GG BB
                var r = (byte)((val >> 16) & 0xFF);
                var g = (byte)((val >> 8) & 0xFF);
                var b = (byte)(val & 0xFF);

                quantizedColors[(i * 3) + 0] = r;
                quantizedColors[(i * 3) + 1] = g;
                quantizedColors[(i * 3) + 2] = b;
            }

            System.Runtime.InteropServices.Marshal.Copy(quantizedColors, 0, dest, quantizedColors.Length);
        }

        private void LogDetectionResults(
            float[] detection_classes_out,
            float[] detection_scores_out,
            float[] detection_boxes_out,
            int numDetections)
        {
            for (int i = 0; i < numDetections; i++)
            {
                var score = detection_scores_out[i];
                var classId = (int)detection_classes_out[i];

                var labelIndex = classId + LabelOffset;
                if (labelIndex.Between(0, labels.Length - 1))
                {
                    var label = labels[labelIndex];
                    if (score >= MinScore)
                    {
                        var xmin = detection_boxes_out[0];
                        var ymin = detection_boxes_out[1];
                        var xmax = detection_boxes_out[2];
                        var ymax = detection_boxes_out[3];


                        // TODO
                        //MessagingCenter.Instance.Send(this, nameof(ObjectDetectedMessage), new DetectionMessage
                        //{
                        //    Xmin = xmin,
                        //    Ymin = ymin,
                        //    Xmax = xmax,
                        //    Ymax = ymax,
                        //    Score = score,
                        //    Label = label,
                        //});
                    }
                }
            }
        }
    }
}
