//-----------------------------------------------------------------------
// <copyright file="ErrorMessageViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

namespace Web.Models
{
    public class ErrorMessageViewModel
    {
        private readonly string message;

        public ErrorMessageViewModel(string message)
        {
            this.message = message;
        }

        public string Message
        {
            get { return message; }
        }
    }
}
