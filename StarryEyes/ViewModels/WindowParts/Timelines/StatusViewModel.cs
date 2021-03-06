﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Windows;
using Livet;
using Livet.Commands;
using StarryEyes.Breezy.Authorize;
using StarryEyes.Breezy.DataModel;
using StarryEyes.Breezy.Util;
using StarryEyes.Filters;
using StarryEyes.Models;
using StarryEyes.Models.Backstages.NotificationEvents;
using StarryEyes.Models.Backstages.TwitterEvents;
using StarryEyes.Models.Operations;
using StarryEyes.Models.Stores;
using StarryEyes.Nightmare.Windows;
using StarryEyes.Settings;
using StarryEyes.Views.Messaging;
using StarryEyes.Views.Utils;

namespace StarryEyes.ViewModels.WindowParts.Timelines
{
    public class StatusViewModel : ViewModel
    {
        private readonly ReadOnlyDispatcherCollectionRx<UserViewModel> _favoritedUsers;
        private readonly TimelineViewModelBase _parent;
        private readonly ReadOnlyDispatcherCollectionRx<UserViewModel> _retweetedUsers;
        private long[] _bindingAccounts;
        private TwitterStatus _inReplyTo;
        private bool _isSelected;
        private UserViewModel _recipient;
        private UserViewModel _retweeter;
        private UserViewModel _user;

        public StatusViewModel(StatusModel status)
            : this(null, status, null)
        {
        }

        public StatusViewModel(TimelineViewModelBase parent, StatusModel status,
                               IEnumerable<long> initialBoundAccounts)
        {
            _parent = parent;
            // get status model
            Model = status;
            RetweetedOriginalModel = status.RetweetedOriginal;

            // bind accounts 
            _bindingAccounts = initialBoundAccounts.Guard().ToArray();

            // initialize users information
            _favoritedUsers = ViewModelHelperRx.CreateReadOnlyDispatcherCollectionRx(
                Model.FavoritedUsers, user => new UserViewModel(user), DispatcherHelper.UIDispatcher);
            CompositeDisposable.Add(_favoritedUsers);
            CompositeDisposable.Add(
                _favoritedUsers.ListenCollectionChanged()
                               .Subscribe(_ =>
                               {
                                   RaisePropertyChanged(() => IsFavorited);
                                   RaisePropertyChanged(() => IsFavoritedUserExists);
                                   RaisePropertyChanged(() => FavoriteCount);
                               }));
            _retweetedUsers = ViewModelHelperRx.CreateReadOnlyDispatcherCollectionRx(
                Model.RetweetedUsers, user => new UserViewModel(user), DispatcherHelper.UIDispatcher);
            CompositeDisposable.Add(_retweetedUsers);
            CompositeDisposable.Add(
                _retweetedUsers.ListenCollectionChanged()
                               .Subscribe(_ =>
                               {
                                   RaisePropertyChanged(() => IsRetweeted);
                                   RaisePropertyChanged(() => IsRetweetedUserExists);
                                   RaisePropertyChanged(() => RetweetCount);
                               }));
            if (RetweetedOriginalModel != null)
            {
                CompositeDisposable.Add(
                            RetweetedOriginalModel.FavoritedUsers.ListenCollectionChanged()
                                                  .Subscribe(_ => this.RaisePropertyChanged(() => IsFavorited)));
                CompositeDisposable.Add(
                    RetweetedOriginalModel.RetweetedUsers.ListenCollectionChanged()
                                          .Subscribe(_ => this.RaisePropertyChanged(() => IsRetweeted)));
            }

            // resolve images
            var imgsubj = Model.ImagesSubject;
            if (imgsubj != null)
            {
                lock (imgsubj)
                {
                    var subscribe = imgsubj
                        .Finally(() =>
                        {
                            RaisePropertyChanged(() => Images);
                            RaisePropertyChanged(() => FirstImage);
                            RaisePropertyChanged(() => IsImageAvailable);
                        })
                        .Subscribe();
                    CompositeDisposable.Add(subscribe);
                }
            }

            // look-up in-reply-to
            if (!status.Status.InReplyToStatusId.HasValue) return;
            var inReplyTo = StoreHelper.GetTweet(status.Status.InReplyToStatusId.Value)
                                       .Subscribe(replyTo =>
                                       {
                                           this._inReplyTo = replyTo;
                                           this.RaisePropertyChanged(() => this.IsInReplyToExists);
                                           this.RaisePropertyChanged(() => this.InReplyToUserImage);
                                           this.RaisePropertyChanged(() => this.InReplyToUserName);
                                           this.RaisePropertyChanged(() => this.InReplyToUserScreenName);
                                           this.RaisePropertyChanged(() => this.InReplyToBody);
                                       });
            this.CompositeDisposable.Add(inReplyTo);

        }


        public TimelineViewModelBase Parent
        {
            get { return _parent; }
        }

        /// <summary>
        ///     Represents status model.
        /// </summary>
        public StatusModel Model { get; private set; }

        public StatusModel RetweetedOriginalModel { get; private set; }

        /// <summary>
        ///     Represents ORIGINAL status. 
        ///     (if this status is retweet, this property represents a status which contains retweeted_original.)
        /// </summary>
        public TwitterStatus OriginalStatus
        {
            get { return Model.Status; }
        }

        /// <summary>
        ///     Represents status. (if this status is retweet, this property represents retweeted_original.)
        /// </summary>
        public TwitterStatus Status
        {
            get
            {
                return this.Model.Status.RetweetedOriginal ?? this.Model.Status;
            }
        }

        public IEnumerable<long> BindingAccounts
        {
            get { return _bindingAccounts; }
            set
            {
                _bindingAccounts = value.ToArray();
                // raise property changed
                RaisePropertyChanged();
                RaisePropertyChanged(() => IsFavorited);
                RaisePropertyChanged(() => IsRetweeted);
                RaisePropertyChanged(() => IsMyselfStrict);
            }
        }

        public UserViewModel User
        {
            get
            {
                return _user ??
                       (_user = new UserViewModel((Status.RetweetedOriginal ?? Status).User));
            }
        }

        public UserViewModel Retweeter
        {
            get { return _retweeter ?? (_retweeter = new UserViewModel(OriginalStatus.User)); }
        }

        public UserViewModel Recipient
        {
            get { return _recipient ?? (_recipient = new UserViewModel(Status.Recipient)); }
        }

        public bool IsRetweetedUserExists
        {
            get { return _retweetedUsers.Count > 0; }
        }

        public int RetweetCount
        {
            get { return RetweetedUsers.Count; }
        }

        public ReadOnlyDispatcherCollectionRx<UserViewModel> RetweetedUsers
        {
            get { return _retweetedUsers; }
        }

        public bool IsFavoritedUserExists
        {
            get { return _favoritedUsers.Count > 0; }
        }

        public int FavoriteCount
        {
            get { return FavoritedUsers.Count; }
        }

        public ReadOnlyDispatcherCollectionRx<UserViewModel> FavoritedUsers
        {
            get { return _favoritedUsers; }
        }

        public bool IsDirectMessage
        {
            get { return Status.StatusType == StatusType.DirectMessage; }
        }

        public bool IsRetweet
        {
            get { return OriginalStatus.RetweetedOriginal != null; }
        }

        public bool IsFavorited
        {
            get
            {
                return this.RetweetedOriginalModel != null
                           ? this.RetweetedOriginalModel.IsFavorited(this._bindingAccounts)
                           : this.Model.IsFavorited(this._bindingAccounts);
            }
        }

        public bool IsRetweeted
        {
            get
            {
                return this.RetweetedOriginalModel != null
                           ? this.RetweetedOriginalModel.IsRetweeted(this._bindingAccounts)
                           : this.Model.IsRetweeted(this._bindingAccounts);
            }
        }

        public bool CanFavoriteAndRetweet
        {
            get { return CanFavoriteImmediate && CanRetweetImmediate; }
        }

        public bool CanFavorite
        {
            get { return !IsDirectMessage && (Setting.AllowFavoriteMyself.Value || !IsMyself); }
        }

        public bool CanFavoriteImmediate
        {
            get { return CanFavorite; }
        }

        public bool CanRetweet
        {
            get { return !IsDirectMessage && !Status.User.IsProtected; }
        }

        public bool CanRetweetImmediate
        {
            get { return CanRetweet && !IsMyselfStrict; }
        }

        public bool CanDelete
        {
            get { return IsDirectMessage || AccountsStore.AccountIds.Contains(OriginalStatus.User.Id); }
        }

        public bool IsMyself
        {
            get { return AccountsStore.AccountIds.Contains(OriginalStatus.User.Id); }
        }

        public bool IsMyselfStrict
        {
            get { return CheckUserIsBind(Status.User.Id); }
        }

        private bool CheckUserIsBind(long id)
        {
            return _bindingAccounts.Length == 1 && _bindingAccounts[0] == id;
        }

        public bool IsInReplyToExists
        {
            get { return _inReplyTo != null; }
        }

        public bool IsInReplyToMe
        {
            get
            {
                return FilterSystemUtil.InReplyToUsers(Status)
                                       .Any(AccountsStore.AccountIds.Contains);
            }
        }

        public Uri InReplyToUserImage
        {
            get
            {
                if (_inReplyTo == null) return null;
                return _inReplyTo.User.ProfileImageUri;
            }
        }

        public string InReplyToUserName
        {
            get
            {
                if (_inReplyTo == null) return null;
                return _inReplyTo.User.Name;
            }
        }

        public string InReplyToUserScreenName
        {
            get
            {
                if (_inReplyTo == null)
                    return Status.InReplyToScreenName;
                return _inReplyTo.User.ScreenName;
            }
        }

        public string InReplyToBody
        {
            get
            {
                if (_inReplyTo == null) return null;
                return _inReplyTo.Text;
            }
        }

        public bool IsFocused
        {
            get { return _parent.FocusedStatus == this; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (this._isSelected == value || this.Parent == null) return;
                this._isSelected = value;
                this.RaisePropertyChanged(() => this.IsSelected);
                this.Parent.OnSelectionUpdated();
            }
        }

        public bool IsSourceVisible
        {
            get { return Status.StatusType != StatusType.DirectMessage; }
        }

        public bool IsSourceIsLink
        {
            get { return Status.Source != null && Status.Source.Contains("<a href"); }
        }

        public string SourceText
        {
            get
            {
                if (IsSourceIsLink)
                {
                    var start = Status.Source.IndexOf(">", StringComparison.Ordinal);
                    var end = Status.Source.IndexOf("<", start + 1, StringComparison.Ordinal);
                    if (start >= 0 && end >= 0)
                    {
                        return Status.Source.Substring(start + 1, end - start - 1);
                    }
                }
                return Status.Source;
            }
        }

        public DateTime CreatedAt
        {
            get { return Status.CreatedAt; }
        }

        public bool IsImageAvailable
        {
            get { return Model.Images != null && Model.Images.Any(); }
        }

        public IEnumerable<Uri> Images
        {
            get { return Model.Images.Select(i => i.Item2); }
        }

        public Uri FirstImage
        {
            get { return Model.Images != null ? Model.Images.Select(i => i.Item2).FirstOrDefault() : null; }
        }

        /// <summary>
        ///     For animating helper
        /// </summary>
        internal bool IsLoaded { get; set; }

        public void RaiseFocusedChanged()
        {
            RaisePropertyChanged(() => IsFocused);
            if (IsFocused)
            {
                Messenger.Raise(new BringIntoViewMessage());
            }
        }

        public void ShowUserProfile()
        {
            SearchFlipModel.RequestSearch(this.User.ScreenName, SearchMode.UserScreenName);
        }

        public void ShowRetweeterProfile()
        {
            SearchFlipModel.RequestSearch(this.Retweeter.ScreenName, SearchMode.UserScreenName);
        }

        public void OpenWeb()
        {
            BrowserHelper.Open(Status.Permalink);
        }

        public void OpenFavstar()
        {
            BrowserHelper.Open(Status.FavstarPermalink);
        }

        public void OpenUserDetailOnTwitter()
        {
            User.OpenUserDetailOnTwitter();
        }

        public void OpenUserFavstar()
        {
            User.OpenUserFavstar();
        }

        public void OpenUserTwilog()
        {
            User.OpenUserTwilog();
        }

        public void OpenSourceLink()
        {
            if (!IsSourceIsLink) return;
            var start = Status.Source.IndexOf("\"", StringComparison.Ordinal);
            var end = Status.Source.IndexOf("\"", start + 1, StringComparison.Ordinal);
            if (start < 0 || end < 0) return;
            var url = this.Status.Source.Substring(start + 1, end - start - 1);
            BrowserHelper.Open(url);
        }

        public void OpenFirstImage()
        {
            if (Model.Images == null) return;
            var tuple = Model.Images.FirstOrDefault();
            if (tuple == null) return;
            BrowserHelper.Open(tuple.Item1);
        }

        #region Text selection control

        private string _selectedText;
        public string SelectedText
        {
            get { return this._selectedText ?? String.Empty; }
            set
            {
                this._selectedText = value;
                this.RaisePropertyChanged();
            }
        }

        public void CopyText()
        {
            // ReSharper disable EmptyGeneralCatchClause
            try
            {
                Clipboard.SetText(SelectedText);
            }
            catch
            {
            }
            // ReSharper restore EmptyGeneralCatchClause
        }

        public void SetTextToInputBox()
        {
            InputAreaModel.SetText(body: SelectedText);
        }

        public void FindOnKrile()
        {
            SearchFlipModel.RequestSearch(SelectedText, SearchMode.Local);
        }

        public void FindOnTwitter()
        {
            SearchFlipModel.RequestSearch(SelectedText, SearchMode.Web);
        }

        private const string GoogleUrl = @"http://www.google.com/search?q={0}";
        public void FindOnGoogle()
        {
            var encoded = HttpUtility.UrlEncode(SelectedText);
            var url = String.Format(GoogleUrl, encoded);
            BrowserHelper.Open(url);
        }

        #endregion

        #region Execution commands

        public void CopyBody()
        {
            SetClipboard(Status.GetEntityAidedText(true));
        }

        public void CopyPermalink()
        {
            SetClipboard(Status.Permalink);
        }

        public void CopySTOT()
        {
            SetClipboard(Status.STOTString);
        }

        private void SetClipboard(string value)
        {
            try
            {
                Clipboard.SetText(value);
            }
            catch (Exception ex)
            {
                var msg = new TaskDialogMessage(new TaskDialogOptions
                            {
                                CommonButtons = TaskDialogCommonButtons.Close,
                                MainIcon = VistaTaskDialogIcon.Error,
                                MainInstruction = "コピーを行えませんでした。",
                                Content = ex.Message,
                                Title = "クリップボード エラー"
                            });
                this.Parent.Messenger.Raise(msg);
            }
        }

        public void Favorite(IEnumerable<AuthenticateInfo> infos, bool add)
        {
            Action<AuthenticateInfo> expected;
            Action<AuthenticateInfo> onFail;
            if (add)
            {
                expected = a => Model.AddFavoritedUser(a.Id);
                onFail = a => Model.RemoveFavoritedUser(a.Id);
            }
            else
            {
                expected = a => Model.RemoveFavoritedUser(a.Id);
                onFail = a => Model.AddFavoritedUser(a.Id);
            }

            infos.ToObservable()
                 .Do(expected)
                 .Do(_ => RaisePropertyChanged(() => IsFavorited))
                 .SelectMany(a => new FavoriteOperation(a, Status, add)
                                      .Run()
                                      .Catch((Exception ex) =>
                                      {
                                          onFail(a);
                                          BackstageModel.RegisterEvent(
                                              new OperationFailedEvent((add ? "" : "un") + "favorite failed: " +
                                                                       a.UnreliableScreenName + " -> " +
                                                                       Status.User.ScreenName + " :" +
                                                                       ex.Message));
                                          return Observable.Empty<TwitterStatus>();
                                      }))
                 .Do(_ => RaisePropertyChanged(() => IsFavorited))
                 .Subscribe();
        }

        public void Retweet(IEnumerable<AuthenticateInfo> infos, bool add)
        {
            Action<AuthenticateInfo> expected;
            Action<AuthenticateInfo> onFail;
            if (add)
            {
                expected = a => Model.AddRetweetedUser(a.Id);
                onFail = a => Model.RemoveRetweetedUser(a.Id);
            }
            else
            {
                expected = a => Model.RemoveRetweetedUser(a.Id);
                onFail = a => Model.AddRetweetedUser(a.Id);
            }
            infos.ToObservable()
                 .Do(expected)
                 .Do(_ => RaisePropertyChanged(() => IsRetweeted))
                 .SelectMany(a => new RetweetOperation(a, Status, add)
                                      .Run()
                                      .Catch((Exception ex) =>
                                      {
                                          onFail(a);
                                          BackstageModel.RegisterEvent(
                                              new OperationFailedEvent((add ? "" : "un") + "retweet failed: " +
                                                                       a.UnreliableScreenName + " -> " +
                                                                       Status.User.ScreenName + " :" +
                                                                       ex.Message));
                                          return Observable.Empty<TwitterStatus>();
                                      }))
                 .Do(_ => RaisePropertyChanged(() => IsRetweeted))
                 .Subscribe();
        }

        public void ToggleFavoriteImmediate()
        {
            if (!AssertQuickActionEnabled()) return;
            if (IsDirectMessage)
            {
                NotifyQuickActionFailed("このツイートはお気に入り登録できません。",
                                        "ダイレクトメッセージはお気に入り登録できません。");
                return;
            }
            if (!CanFavoriteImmediate && !IsFavorited)
            {
                NotifyQuickActionFailed("このツイートはお気に入り登録できません。",
                                        "自分自身のツイートをお気に入り登録しないよう設定されています。");
                return;
            }
            Favorite(GetImmediateAccounts(), !IsFavorited);
        }

        public void ToggleRetweetImmediate()
        {
            if (!AssertQuickActionEnabled()) return;
            if (!CanRetweetImmediate)
            {
                NotifyQuickActionFailed("このツイートは現在のアカウントからリツイートできません。",
                    "自分自身のツイートはリツイートできません。");
                return;
            }
            Retweet(GetImmediateAccounts(), !IsRetweeted);
        }

        private bool AssertQuickActionEnabled()
        {
            if (!BindingAccounts.Any())
            {
                NotifyQuickActionFailed("アカウントが選択されていません。",
                                        "クイックアクションを利用するには、投稿欄横のエリアからアカウントを選択する必要があります。" + Environment.NewLine +
                                        "選択されているアカウントはタブごとに保持されます。");
                return false;
            }
            return true;
        }

        private void NotifyQuickActionFailed(string main, string body)
        {
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                CommonButtons = TaskDialogCommonButtons.Close,
                MainIcon = VistaTaskDialogIcon.Error,
                MainInstruction = main,
                Content = body,
                Title = "クイックアクション エラー"
            });
            this.Parent.Messenger.Raise(msg);
        }

        public void FavoriteAndRetweetImmediate()
        {
            if (!AssertQuickActionEnabled()) return;
            var accounts = GetImmediateAccounts()
                .ToObservable()
                .Publish();
            if (!IsFavorited)
            {
                accounts.Do(a => Model.AddFavoritedUser(a.Id))
                        .Do(_ => RaisePropertyChanged(() => IsFavorited))
                        .SelectMany(a => new FavoriteOperation(a, Status, true)
                                             .Run()
                                             .Catch((Exception ex) =>
                                             {
                                                 Model.RemoveFavoritedUser(a.Id);
                                                 return Observable.Empty<TwitterStatus>();
                                             }))
                        .Do(_ => RaisePropertyChanged(() => IsFavorited))
                        .Subscribe();
            }
            if (!IsRetweeted)
            {
                accounts.Do(a => Model.AddRetweetedUser(a.Id))
                          .Do(_ => RaisePropertyChanged(() => IsRetweeted))
                          .SelectMany(a => new RetweetOperation(a, Status, true)
                                               .Run()
                                               .Catch((Exception ex) =>
                                               {
                                                   Model.RemoveRetweetedUser(a.Id);
                                                   return Observable.Empty<TwitterStatus>();
                                               }))
                          .Do(_ => RaisePropertyChanged(() => IsRetweeted))
                          .Subscribe();
            }
            accounts.Connect();
        }

        private IEnumerable<AuthenticateInfo> GetImmediateAccounts()
        {
            return AccountsStore.Accounts
                                .Where(a => _bindingAccounts.Contains(a.UserId))
                                .Select(a => a.AuthenticateInfo);
        }

        public void ToggleSelect()
        {
            IsSelected = !IsSelected;
        }

        public void ToggleFavorite()
        {
            if (!CanFavorite)
            {
                NotifyQuickActionFailed("このツイートはお気に入り登録できません。",
                                        IsDirectMessage ? "ダイレクトメッセージはお気に入り登録できません。" :
                                        "自分自身のツイートをお気に入り登録しないよう設定されています。");
                return;
            }
            var favoriteds =
                AccountsStore.Accounts
                             .Where(a => Model.IsFavorited(a.UserId))
                             .Select(a => a.AuthenticateInfo)
                             .ToArray();
            MainWindowModel.ExecuteAccountSelectAction(
                AccountSelectionAction.Favorite,
                favoriteds,
                infos =>
                {
                    var authenticateInfos =
                        infos as AuthenticateInfo[] ?? infos.ToArray();
                    var adds =
                        authenticateInfos.Except(favoriteds);
                    var rmvs =
                        favoriteds.Except(authenticateInfos);
                    Favorite(adds, true);
                    Favorite(rmvs, false);
                });
        }

        public void ToggleRetweet()
        {
            if (!CanRetweet)
            {
                return;
            }
            var retweeteds =
                AccountsStore.Accounts
                             .Where(a => Model.IsRetweeted(a.UserId))
                             .Select(a => a.AuthenticateInfo)
                             .ToArray();
            MainWindowModel.ExecuteAccountSelectAction(
                AccountSelectionAction.Retweet,
                retweeteds,
                infos =>
                {
                    var authenticateInfos =
                        infos as AuthenticateInfo[] ?? infos.ToArray();
                    var adds =
                        authenticateInfos.Except(retweeteds);
                    var rmvs =
                        retweeteds.Except(authenticateInfos);
                    Retweet(adds, true);
                    Retweet(rmvs, false);
                });
        }

        public void SendReply()
        {
            if (Status.StatusType == StatusType.DirectMessage)
            {
                DirectMessage();
            }
            else
            {
                Reply();
            }
        }

        public void Reply()
        {
            if (IsSelected)
            {
                Parent.ReplySelecteds();
            }
            else
            {
                InputAreaModel.SetText(Model.GetSuitableReplyAccount(), "@" + User.ScreenName + " ", inReplyTo: Status);
            }
        }

        public void Quote()
        {
            InputAreaModel.SetText(Model.GetSuitableReplyAccount(),
                " RT @" + User.ScreenName + " " + Status.GetEntityAidedText(true), CursorPosition.Begin);
        }

        public void QuotePermalink()
        {
            InputAreaModel.SetText(Model.GetSuitableReplyAccount(), " " + Status.Permalink, CursorPosition.Begin);
        }

        public void DirectMessage()
        {
            InputAreaModel.SetDirectMessage(Model.GetSuitableReplyAccount(), Status.User);
        }

        public void ConfirmDelete()
        {
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                AllowDialogCancellation = true,
                CustomButtons = new[] { "削除", "キャンセル" },
                Content = "削除したツイートはもとに戻せません。",
                FooterIcon = VistaTaskDialogIcon.Information,
                MainIcon = VistaTaskDialogIcon.Warning,
                MainInstruction = "ツイートを削除しますか？",
                FooterText = "直近一件のツイートの訂正は、投稿欄で↑キーを押すと行えます。",
                Title = "ツイートの削除",
            });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.CustomButtonResult == 0)
            {
                Delete();
            }
        }

        public void Delete()
        {
            AuthenticateInfo info = null;
            if (IsDirectMessage)
            {
                var ids = new[] { Status.User.Id, Status.Recipient.Id };
                info = ids
                    .Select(AccountsStore.GetAccountSetting)
                    .Where(_ => _ != null)
                    .Select(_ => _.AuthenticateInfo)
                    .FirstOrDefault();
            }
            else
            {
                var ai = AccountsStore.GetAccountSetting(OriginalStatus.User.Id);
                if (ai != null)
                {
                    info = ai.AuthenticateInfo;
                }
            }
            if (info != null)
            {
                new DeleteOperation(info, OriginalStatus)
                    .Run()
                    .Subscribe(_ => StatusStore.Remove(_.Id),
                               ex =>
                               BackstageModel.RegisterEvent(new OperationFailedEvent("ツイートを削除できませんでした: " + ex.Message)));
            }
        }

        private bool _lastSelectState;
        public void ToggleFocus()
        {
            var psel = _lastSelectState;
            _lastSelectState = IsSelected;
            if (psel != IsSelected) return;
            Parent.FocusedStatus =
                Parent.FocusedStatus == this ? null : this;
        }

        public void ShowConversation()
        {
            // TODO: Implementation
        }

        public void GiveFavstarTrophy()
        {
            if (!AssertQuickActionEnabled()) return;
            if (Setting.FavstarApiKey.Value == null)
            {
                this.Parent.Messenger.Raise(new TaskDialogMessage(new TaskDialogOptions
                {
                    AllowDialogCancellation = true,
                    CommonButtons = TaskDialogCommonButtons.Close,
                    MainIcon = VistaTaskDialogIcon.Error,
                    MainInstruction = "この操作にはFavstar APIキーが必要です。",
                    Content = "Favstar APIキーを取得し、設定画面で登録してください。",
                    FooterIcon = VistaTaskDialogIcon.Information,
                    FooterText = "FavstarのProメンバーのみこの操作を行えます。",
                    Title = "ツイート賞の授与",
                }));
                return;
            }
            var msg = new TaskDialogMessage(new TaskDialogOptions
                        {
                            AllowDialogCancellation = true,
                            CommonButtons = TaskDialogCommonButtons.OKCancel,
                            MainIcon = VistaTaskDialogIcon.Information,
                            MainInstruction = "このツイートに今日のツイート賞を与えますか？",
                            Content = Status.ToString(),
                            FooterIcon = VistaTaskDialogIcon.Information,
                            FooterText = "FavstarのProメンバーのみこの操作を行えます。",
                            Title = "Favstar ツイート賞の授与",
                        });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.Result != TaskDialogSimpleResult.Ok) return;
            var accounts = this.GetImmediateAccounts()
                .ToObservable();
            accounts.SelectMany(a => new FavstarTrophyOperation(a, this.Status).Run())
                    .Do(_ => this.RaisePropertyChanged(() => this.IsFavorited))
                    .Subscribe();
        }

        public void ReportAsSpam()
        {
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                AllowDialogCancellation = true,
                CustomButtons = new[] { "スパム報告", "キャンセル" },
                MainIcon = VistaTaskDialogIcon.Warning,
                MainInstruction = "ユーザー " + Status.User.ScreenName + " をスパム報告しますか？",
                Content = "全てのアカウントからブロックし、代表のアカウントからスパム報告します。",
                Title = "ユーザーをスパムとして報告",
            });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.CustomButtonResult != 0) return;
            // report as a spam
            var accounts = AccountsStore.Accounts.ToArray();
            var reporter = accounts.FirstOrDefault();
            if (reporter == null) return;
            accounts.Select(a => a.AuthenticateInfo)
                    .Select(a => new UpdateRelationOperation(a, this.Status.User, RelationKind.Block))
                    .Append(new UpdateRelationOperation(reporter.AuthenticateInfo, this.Status.User,
                                                        RelationKind.ReportAsSpam))
                    .ToObservable()
                    .SelectMany(
                        o => o.Run()
                              .Do(
                                  r =>
                                  BackstageModel.RegisterEvent(new BlockedEvent(o.Info.UserInfo, o.Target))))
                    .Subscribe(
                        _ => { },
                        ex => BackstageModel.RegisterEvent(new InternalErrorEvent(ex.Message)),
                        () => StatusStore.Find(
                            s =>
                            s.User.Id == this.Status.User.Id ||
                            (s.RetweetedOriginal != null && s.RetweetedOriginal.User.Id == this.Status.User.Id))
                                         .Subscribe(s => StatusStore.Remove(s.Id)));
        }

        public void MuteKeyword()
        {
            if (String.IsNullOrWhiteSpace(SelectedText))
            {
                this.Parent.Messenger.Raise(new TaskDialogMessage(new TaskDialogOptions
                {
                    CommonButtons = TaskDialogCommonButtons.Close,
                    MainIcon = VistaTaskDialogIcon.Information,
                    MainInstruction = "キーワードを選択してください。",
                    Content = "ミュートしたいキーワードをドラッグで選択できます。",
                    Title = "キーワードのミュート",
                }));
            }
            // TODO
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                AllowDialogCancellation = true,
                CustomButtons = new[] { "ミュート", "キャンセル" },
                MainIcon = VistaTaskDialogIcon.Warning,
                MainInstruction = "キーワード " + SelectedText + " をミュートしますか？",
                Content = "このキーワードを含むツイートが全てのタブから除外されるようになります。",
                FooterIcon = VistaTaskDialogIcon.Information,
                FooterText = "ミュートの解除は設定画面から行えます。",
                Title = "キーワードミュート",
            });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.CustomButtonResult != 0) return;
            // TODO: Mute
            System.Diagnostics.Debug.WriteLine("Mute: " + Status.User.ScreenName);
        }

        public void MuteUser()
        {
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                AllowDialogCancellation = true,
                CustomButtons = new[] { "ミュート", "キャンセル" },
                MainIcon = VistaTaskDialogIcon.Warning,
                MainInstruction = "ユーザー " + Status.User.ScreenName + " をミュートしますか？",
                Content = "このユーザーのツイートが全てのタブから除外されるようになります。",
                FooterIcon = VistaTaskDialogIcon.Information,
                FooterText = "ミュートの解除は設定画面から行えます。",
                Title = "ユーザーのミュート",
            });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.CustomButtonResult != 0) return;
            // TODO: Mute
            System.Diagnostics.Debug.WriteLine("Mute: " + Status.User.ScreenName);
        }

        public void MuteClient()
        {
            var msg = new TaskDialogMessage(new TaskDialogOptions
            {
                AllowDialogCancellation = true,
                CommonButtons = TaskDialogCommonButtons.OKCancel,
                MainIcon = VistaTaskDialogIcon.Warning,
                MainInstruction = "クライアント " + SourceText + " をミュートしますか？",
                Content = "このクライアントからのツイートが全てのタブから除外されるようになります。",
                FooterIcon = VistaTaskDialogIcon.Information,
                FooterText = "ミュートの解除は設定画面から行えます。",
                Title = "クライアントのミュート",
            });
            var response = this.Parent.Messenger.GetResponse(msg);
            if (response.Response.Result == TaskDialogSimpleResult.Ok)
            {
                // report as a spam
                System.Diagnostics.Debug.WriteLine("Mute: " + Status.Source);
            }
        }
        #endregion

        #region OpenLinkCommand

        private ListenerCommand<string> _openLinkCommand;

        public ListenerCommand<string> OpenLinkCommand
        {
            get { return _openLinkCommand ?? (_openLinkCommand = new ListenerCommand<string>(OpenLink)); }
        }

        public void OpenLink(string parameter)
        {
            var param = TextBlockStylizer.ResolveInternalUrl(parameter);
            switch (param.Item1)
            {
                case LinkType.User:
                    SearchFlipModel.RequestSearch(param.Item2, SearchMode.UserScreenName);
                    break;
                case LinkType.Hash:
                    SearchFlipModel.RequestSearch("#" + param.Item2, SearchMode.Web);
                    break;
                case LinkType.Url:
                    BrowserHelper.Open(param.Item2);
                    break;
            }
        }

        #endregion
    }
}