﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Extensions;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Profile;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Rulesets;
using osu.Game.Users;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    public partial class UserProfileOverlay : FullscreenOverlay<ProfileHeader>
    {
        protected override Container<Drawable> Content => onlineViewContainer;

        private readonly OnlineViewContainer onlineViewContainer;
        private readonly LoadingLayer loadingLayer;

        private ProfileSection? lastSection;
        private ProfileSection[]? sections;
        private GetUserRequest? userReq;
        private ProfileSectionsContainer? sectionsContainer;
        private ProfileSectionTabControl? tabs;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        public const float CONTENT_X_MARGIN = 70;

        public UserProfileOverlay()
            : base(OverlayColourScheme.Pink)
        {
            base.Content.AddRange(new Drawable[]
            {
                onlineViewContainer = new OnlineViewContainer($"Sign in to view the {Header.Title.Title}")
                {
                    RelativeSizeAxes = Axes.Both
                },
                loadingLayer = new LoadingLayer(true)
            });
        }

        protected override ProfileHeader CreateHeader() => new ProfileHeader();

        protected override Color4 BackgroundColour => ColourProvider.Background6;

        public void ShowUser(IUser user, IRulesetInfo? ruleset = null)
        {
            if (user.OnlineID == APIUser.SYSTEM_USER_ID)
                return;

            Show();

            if (user.OnlineID == Header.User.Value?.User.Id && ruleset?.MatchesOnlineID(Header.User.Value?.Ruleset) == true)
                return;

            if (sectionsContainer != null)
                sectionsContainer.ExpandableHeader = null;

            userReq?.Cancel();
            Clear();
            lastSection = null;

            sections = !user.IsBot
                ? new ProfileSection[]
                {
                    //new AboutSection(),
                    new RecentSection(),
                    new RanksSection(),
                    //new MedalsSection(),
                    new HistoricalSection(),
                    new BeatmapsSection(),
                    new KudosuSection()
                }
                : Array.Empty<ProfileSection>();

            tabs = new ProfileSectionTabControl
            {
                RelativeSizeAxes = Axes.X,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
            };

            Add(sectionsContainer = new ProfileSectionsContainer
            {
                ExpandableHeader = Header,
                FixedHeader = tabs,
                HeaderBackground = new Box
                {
                    // this is only visible as the ProfileTabControl background
                    Colour = ColourProvider.Background5,
                    RelativeSizeAxes = Axes.Both
                },
            });
            sectionsContainer.SelectedSection.ValueChanged += section =>
            {
                if (lastSection != section.NewValue)
                {
                    lastSection = section.NewValue;
                    tabs.Current.Value = lastSection;
                }
            };

            tabs.Current.ValueChanged += section =>
            {
                if (lastSection == null)
                {
                    lastSection = sectionsContainer.Children.FirstOrDefault();
                    if (lastSection != null)
                        tabs.Current.Value = lastSection;
                    return;
                }

                if (lastSection != section.NewValue)
                {
                    lastSection = section.NewValue;
                    sectionsContainer.ScrollTo(lastSection);
                }
            };

            sectionsContainer.ScrollToTop();

            userReq = user.OnlineID > 1 ? new GetUserRequest(user.OnlineID, ruleset) : new GetUserRequest(user.Username, ruleset);
            userReq.Success += u => userLoadComplete(u, ruleset);
            API.Queue(userReq);
            loadingLayer.Show();
        }

        private void userLoadComplete(APIUser user, IRulesetInfo? ruleset)
        {
            Debug.Assert(sections != null && sectionsContainer != null && tabs != null);

            var actualRuleset = rulesets.GetRuleset(ruleset?.ShortName ?? user.PlayMode).AsNonNull();

            var userProfile = new UserProfileData(user, actualRuleset);
            Header.User.Value = userProfile;

            if (user.ProfileOrder != null)
            {
                foreach (string id in user.ProfileOrder)
                {
                    var sec = sections.FirstOrDefault(s => s.Identifier == id);

                    if (sec != null)
                    {
                        sec.User.Value = userProfile;

                        sectionsContainer.Add(sec);
                        tabs.AddItem(sec);
                    }
                }
            }

            loadingLayer.Hide();
        }

        private partial class ProfileSectionTabControl : OverlayTabControl<ProfileSection>
        {
            private const float bar_height = 2;

            public ProfileSectionTabControl()
            {
                TabContainer.RelativeSizeAxes &= ~Axes.X;
                TabContainer.AutoSizeAxes |= Axes.X;
                TabContainer.Anchor |= Anchor.x1;
                TabContainer.Origin |= Anchor.x1;

                Height = 36 + bar_height;
                BarHeight = bar_height;
            }

            protected override TabItem<ProfileSection> CreateTabItem(ProfileSection value) => new ProfileSectionTabItem(value)
            {
                AccentColour = AccentColour,
            };

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                AccentColour = colourProvider.Highlight1;
            }

            protected override bool OnClick(ClickEvent e) => true;

            protected override bool OnHover(HoverEvent e) => true;

            private partial class ProfileSectionTabItem : OverlayTabItem
            {
                public ProfileSectionTabItem(ProfileSection value)
                    : base(value)
                {
                    Text.Text = value.Title;
                    Text.Font = Text.Font.With(size: 16);
                    Text.Margin = new MarginPadding { Bottom = 10 + bar_height };
                    Bar.ExpandedSize = 10;
                    Bar.Margin = new MarginPadding { Bottom = bar_height };
                }
            }
        }

        private partial class ProfileSectionsContainer : SectionsContainer<ProfileSection>
        {
            public ProfileSectionsContainer()
            {
                RelativeSizeAxes = Axes.Both;
            }

            protected override UserTrackingScrollContainer CreateScrollContainer() => new OverlayScrollContainer();

            // Reverse child ID is required so expanding beatmap panels can appear above sections below them.
            // This can also be done by setting Depth when adding new sections above if using ReverseChildID turns out to have any issues.
            protected override FlowContainer<ProfileSection> CreateScrollContentContainer() => new ReverseChildIDFillFlowContainer<ProfileSection>
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Spacing = new Vector2(0, 20),
            };
        }
    }
}
