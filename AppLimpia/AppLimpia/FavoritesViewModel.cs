﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;

using AppLimpia.Json;

using Xamarin.Forms;

namespace AppLimpia
{
    /// <summary>
    /// The ViewModel for the Favorites view.
    /// </summary>
    public class FavoritesViewModel : ViewModelBase
    {
        /// <summary>
        /// The primary favorite.
        /// </summary>
        private FavoriteWrapper primaryFavorite;

        /// <summary>
        /// Initializes a new instance of the <see cref="FavoritesViewModel"/> class.
        /// </summary>
        /// <param name="favorites">The user favorites.</param>
        /// <param name="primaryFavorite">The primary user favorite.</param>
        public FavoritesViewModel(IEnumerable<MapExPin> favorites, MapExPin primaryFavorite)
        {
            // Get the favorites collection
            this.Favorites = new ObservableCollection<FavoriteWrapper>();
            foreach (var favorite in favorites)
            {
                // Create the favorite wrapper for UI
                var wrapper = new FavoriteWrapper(favorite, this);
                this.Favorites.Add(wrapper);
                wrapper.PropertyChanged += this.OnFavoritePropertyChanged;

                // If the favorite is a primary favorite
                if (favorite == primaryFavorite)
                {
                    wrapper.IsPrimary = true;
                }
            }

            // Setup commands
            this.CancelCommand = new Command(this.Cancel);
        }

        /// <summary>
        /// Gets the favorite pins.
        /// </summary>
        public ObservableCollection<FavoriteWrapper> Favorites { get; }

        /// <summary>
        /// Gets the primary favorite.
        /// </summary>
        public FavoriteWrapper PrimaryFavorite
        {
            get
            {
                return this.primaryFavorite;
            }

            private set
            {
                // Set the current primary favorite as not favorite
                if (this.primaryFavorite != null)
                {
                    this.primaryFavorite.IsPrimary = false;
                }

                // Change the primary favorite
                this.SetProperty(ref this.primaryFavorite, value, nameof(this.PrimaryFavorite));
                Debug.WriteLine("New primary = {0}", this.primaryFavorite?.Label);
            }
        }

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public ICommand CancelCommand { get; private set; }

        /// <summary>
        /// Handles the PropertyChanged event of favorite.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="PropertyChangedEventArgs"/> with arguments of the event.</param>
        private void OnFavoritePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var favorite = sender as FavoriteWrapper;
            if (favorite != null)
            {
                // If the favorite is a new primary
                if (e.PropertyName == FavoriteWrapper.IsPrimaryProperty.PropertyName)
                {
                    if (favorite.IsPrimary)
                    {
                        // Change the primary favorite
                        this.PrimaryFavorite = favorite;
                    }
                }
            }
        }

        /// <summary>
        /// Cancels the user data update task.
        /// </summary>
        private void Cancel()
        {
            // Return to login view
            this.Navigation.PopModalAsync();
        }

        /// <summary>
        /// The favorite pin wrapper.
        /// </summary>
        public class FavoriteWrapper : BindableObject
        {
            /// <summary>
            /// The is primary property of the map pin.
            /// </summary>
            // ReSharper disable once MemberCanBePrivate.Global
            public static readonly BindableProperty IsPrimaryProperty = BindableProperty.Create(
                "IsPrimary",
                typeof(bool),
                typeof(FavoriteWrapper),
                false);

            /// <summary>
            /// The favorite map pin.
            /// </summary>
            private readonly MapExPin favorite;

            /// <summary>
            /// The reference to the current view model.
            /// </summary>
            private readonly FavoritesViewModel viewModel;

            /// <summary>
            /// Initializes a new instance of the <see cref="FavoriteWrapper"/> class.
            /// </summary>
            /// <param name="favorite">The favorite map pin.</param>
            /// <param name="viewModel">The reference to the current view model.</param>
            public FavoriteWrapper(MapExPin favorite, FavoritesViewModel viewModel)
            {
                // Store the current favorite
                this.favorite = favorite;
                this.viewModel = viewModel;

                // Setup commands
                this.SetAsPrimaryCommand = new Command(this.SetAsPrimary);
                this.RemoveFavoriteCommand = new Command(this.RemoveFavorite);
            }

            /// <summary>
            /// Gets the pin for the currently wrapped favorite.
            /// </summary>
            public MapExPin Pin => this.favorite;

            /// <summary>
            /// Gets the label of the current <see cref="MapExPin"/>.
            /// </summary>
            public string Label => this.favorite.Label;

            /// <summary>
            /// Gets the set as primary command.
            /// </summary>
            public ICommand SetAsPrimaryCommand { get; }

            /// <summary>
            /// Gets the set as primary command.
            /// </summary>
            public ICommand RemoveFavoriteCommand { get; }

            /// <summary>
            /// Gets or sets a value indicating whether the current favorite is a primary favorite.
            /// </summary>
            public bool IsPrimary
            {
                get
                {
                    return (bool)this.GetValue(FavoriteWrapper.IsPrimaryProperty);
                }

                set
                {
                    this.SetValue(FavoriteWrapper.IsPrimaryProperty, value);
                }
            }

            /// <summary>
            /// Sets the current favorite as primary.
            /// </summary>
            private void SetAsPrimary()
            {
                // If the current view model is busy do nothing
                if (this.viewModel.IsBusy)
                {
                    return;
                }

                // Prepare the data to be send to the server
                var request = new Json.JsonObject { { "montonera", this.favorite.Id }, { "principal", true } };

                // Send request to the server
                this.viewModel.IsBusy = true;
                WebHelper.SendAsync(
                    Uris.GetAddToFavoritesUri(),
                    request.AsHttpContent(),
                    _ =>
                    {
                        this.IsPrimary = true;
                        this.viewModel.IsBusy = false;
                    },
                    () => this.viewModel.IsBusy = false);
            }

            /// <summary>
            /// Removes the current favorite from the favorites list.
            /// </summary>
            private void RemoveFavorite()
            {
                // If the current view model is busy do nothing
                if (this.viewModel.IsBusy)
                {
                    return;
                }

                // Prepare the data to be send to the server
                var request = new Json.JsonObject { { "montonera", this.favorite.Id } };

                // Send request to the server
                this.viewModel.IsBusy = true;
                WebHelper.SendAsync(
                    Uris.GetRemoveFromFavoritesUri(),
                    request.AsHttpContent(),
                    _ =>
                    {
                        this.OnFavoriteRemoved();
                        this.viewModel.IsBusy = false;
                    },
                    () => this.viewModel.IsBusy = false);
            }

            /// <summary>
            /// Called when the current favorite was removed from the server.
            /// </summary>
            private void OnFavoriteRemoved()
            {
                // Remove the favorite from view model
                this.favorite.Type = MapPinType.DropPoint;
                if (this.viewModel.Favorites.Remove(this))
                {
                    // If the current favorite is a primary one
                    if (this.viewModel.PrimaryFavorite == this)
                    {
                        this.viewModel.PrimaryFavorite = null;

                        // Set the new primary id
                        var newPrimary = this.viewModel.Favorites.FirstOrDefault();
                        if (newPrimary != null)
                        {
                            newPrimary.IsPrimary = true;
                        }
                    }
                }
            }
        }
    }
}
