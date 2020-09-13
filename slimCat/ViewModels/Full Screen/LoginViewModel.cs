﻿#region Copyright

// <copyright file="LoginViewModel.cs">
//     Copyright (c) 2013-2015, Justin Kadrovach, All rights reserved.
// 
//     This source is subject to the Simplified BSD License.
//     Please see the License.txt file for more information.
//     All other rights reserved.
// 
//     THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
//     KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//     IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//     PARTICULAR PURPOSE.
// </copyright>

#endregion

namespace slimCat.ViewModels
{
    #region Usings

    using System;
    using System.Windows.Input;
    using Libraries;
    using Microsoft.Practices.Prism.Events;
    using Microsoft.Practices.Unity;
    using Models;
    using Services;
    using Utilities;
    using Views;

    #endregion

    /// <summary>
    ///     The LoginViewModel is responsible for displaying login details to the user.
    ///     Fires off 'LoginEvent' when the user clicks the connect button.
    ///     Responds to the 'LoginCompletedEvent'.
    /// </summary>
    public class LoginViewModel : ViewModelBase
    {
        #region Constants

        internal const string LoginViewName = "LoginView";

        #endregion

        #region Constructors and Destructors

        public LoginViewModel(IChatState chatState)
            : base(chatState)
        {
            try
            {
                model = chatState.Account;
                Container.RegisterType<object, LoginView>(LoginViewName);

                LoggingSection = "login vm";
            }
            catch (Exception ex)
            {
                ex.Source = "Login ViewModel, Init";
                Exceptions.HandleException(ex);
            }
        }

        #endregion

        #region Public Methods and Operators

        public void UpdateCapsLockWarning()
        {
            OnPropertyChanged("ShowCapslockWarning");
        }

        #endregion

        #region Fields

        private readonly IAccount model;

        private RelayCommand login;

        private string relayMessage = Constants.FriendlyName;

        private bool requestIsSent;

        private readonly UserPreferences preferences = SettingsService.Preferences;

        #endregion

        #region Public Properties

        public string AccountName
        {
            get { return model.AccountName; }
            set
            {
                if (model.AccountName == value)
                    return;

                model.AccountName = value;
                OnPropertyChanged();
            }
        }

        public ICommand LoginCommand => login
                                        ?? (login = new RelayCommand(_ => SendTicketRequest(), _ => CanLogin()));

        public string Password
        {
            get { return model.Password; }
            set
            {
                if (model.Password == value)
                    return;

                model.Password = value;
                OnPropertyChanged();
            }
        }

        public bool ShowCapslockWarning => Console.CapsLock;

        public string ServerHost
        {
            get { return model.ServerHost; }
            set
            {
                if (model.ServerHost == value)
                    return;

                model.ServerHost = value;
                OnPropertyChanged();
            }
        }

        public string RelayMessage
        {
            get { return relayMessage; }
            set
            {
                relayMessage = value;
                OnPropertyChanged();
            }
        }

        public bool RequestSent
        {
            get { return requestIsSent; }
            set
            {
                requestIsSent = value;
                OnPropertyChanged();
            }
        }

        public bool Advanced
        {
            get { return preferences.IsAdvanced; }
            set { preferences.IsAdvanced = value; }
        }

        public bool SaveLogin
        {
            get { return preferences.SaveLogin; }
            set { preferences.SaveLogin = value; }
        }

        public bool HasNewUpdate { get; set; }

        public string UpdateName { get; set; }

        public string UpdateLink { get; set; }

        public bool UpdateFailed { get; set; }

        public bool UpdateCompleted { get; set; }

        #endregion

        #region Methods

        private bool CanLogin() => !string.IsNullOrWhiteSpace(AccountName)
                                   && !string.IsNullOrWhiteSpace(Password)
                                   && !RequestSent;

        private void SendTicketRequest()
        {
            RelayMessage = "Logging in ...";
            RequestSent = true;
            Events.GetEvent<LoginEvent>().Publish(true);
            Events.GetEvent<LoginCompleteEvent>().Subscribe(HandleLogin, ThreadOption.UIThread);

            Log("Sending login request");
        }

        private void HandleLogin(bool gotTicket)
        {
            RequestSent = false;

            if (!gotTicket)
            {
                RelayMessage = "Oops!" + " " + model.Error;
                Log("Login unsuccessful");
            }
            else
            {
                Log("Login successful");
                if (SaveLogin)
                {
                    preferences.Username = AccountName;
                    preferences.Password = Password;
                    preferences.Host = ServerHost;
                }
                else
                {
                    preferences.Username = null;
                    preferences.Password = null;
                    preferences.Host = null;
                }

                SettingsService.Preferences = preferences;

                RelayMessage = Constants.FriendlyName;
            }
        }

        #endregion
    }
}