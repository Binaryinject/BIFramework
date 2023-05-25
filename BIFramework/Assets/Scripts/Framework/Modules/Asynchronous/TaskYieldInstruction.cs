﻿/*
 * MIT License
 *
 * Copyright (c) 2018 Clark Yang
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of 
 * this software and associated documentation files (the "Software"), to deal in 
 * the Software without restriction, including without limitation the rights to 
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies 
 * of the Software, and to permit persons to whom the Software is furnished to do so, 
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all 
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
 * SOFTWARE.
 */

using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UnityEngine;

namespace BIFramework.Asynchronous
{
    public class TaskYieldInstruction : CustomYieldInstruction
    {
        private readonly Task task;
        public TaskYieldInstruction(Task task)
        {
            this.task = task;
        }

        public override bool keepWaiting
        {
            get
            {
                if (task.Exception != null)
                    ExceptionDispatchInfo.Capture(task.Exception).Throw();

                return !task.IsCompleted;
            }
        }
    }

    public class TaskYieldInstruction<T> : CustomYieldInstruction
    {
        private readonly Task<T> task;
        public TaskYieldInstruction(Task<T> task)
        {
            this.task = task;
        }

        public override bool keepWaiting
        {
            get
            {
                if (task.Exception != null)
                    ExceptionDispatchInfo.Capture(task.Exception).Throw();

                return !task.IsCompleted;
            }
        }
    }
}