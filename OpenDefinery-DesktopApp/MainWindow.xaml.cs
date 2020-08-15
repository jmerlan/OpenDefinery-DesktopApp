﻿using OpenDefinery;
using RestSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OpenDefinery_DesktopApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Definery Definery { get; set; }
        public static Pager Pagination { get; set; }

        public MainWindow()
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            InitializeComponent();

            // Instantiate a new objects
            Definery = new Definery();
            Pagination = new Pager();

            // Set current pagination fields
            Pagination.CurrentPage = 0;
            Pagination.ItemsPerPage = 50;
            Pagination.Offset = 0;

            // Set up UI elements at launch of app
            AddToCollectionGrid.Visibility = Visibility.Hidden;  // The Add to Collection form
            NewParameterGrid.Visibility = Visibility.Hidden;  // The New Parameter form
            NewCollectionGrid.Visibility = Visibility.Hidden;  // The New Collection form
            BatchUploadGrid.Visibility = Visibility.Hidden;  // The batch upload form
            PagerNextButton.IsEnabled = false;  // Pager
            PagerPreviousButton.IsEnabled = false;  // Pager

            if (string.IsNullOrEmpty(Definery.AuthCode) | string.IsNullOrEmpty(Definery.CsrfToken))
            {
                OverlayGrid.Visibility = Visibility.Visible;
                LoginGrid.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Main method to load all the data from Drupal
        /// </summary>
        private void LoadData()
        {
            if (!string.IsNullOrEmpty(Definery.CsrfToken))
            {
                // Load the data from Drupal
                Definery.Groups = Group.GetAll(Definery);
                Definery.DataTypes = DataType.GetAll(Definery);

                // Sort the lists for future use by UI
                Definery.DataTypes.Sort(delegate (DataType x, DataType y)
                {
                    if (x.Name == null && y.Name == null) return 0;
                    else if (x.Name == null) return -1;
                    else if (y.Name == null) return 1;
                    else return x.Name.CompareTo(y.Name);
                });

                // Display Collections in listbox
                Definery.Collections = Collection.GetAll(Definery);
                CollectionsList.DisplayMemberPath = "Name";
                CollectionsList.ItemsSource = Definery.Collections;

                // Get the parameters of the logged in user by default and display in the DataGrid
                Definery.Parameters = SharedParameter.ByUser(
                    Definery, Definery.CurrentUser.Name, Pagination.ItemsPerPage, Pagination.Offset, true
                    );

                // Update the GUI anytime data is loaded
                RefreshUi();
            }
            else
            {
                // Do nothing for now.
            }
        }

        /// <summary>
        /// Helper method to update the UI for the pagination
        /// </summary>
        /// <param name="pager">The Pager object to update</param>
        /// <param name="incrementChange">The increment in which to update the page number (can be positive or negative). Note the first page number is 0.</param>
        private void UpdatePager(Pager pager, int incrementChange)
        {
            // Increment the current page
            pager.CurrentPage += incrementChange;

            // Set the Offset
            if (pager.CurrentPage == 0)
            {
                // Set the new offset to 0
                pager.Offset = 0;
            }
            if (pager.CurrentPage >= 1)
            {
                // Set the new offset based on the items per page and current page
                pager.Offset = pager.ItemsPerPage * pager.CurrentPage;
            }

            // Enable UI as needed
            if (pager.TotalPages > pager.CurrentPage + 1)
            {
                PagerNextButton.IsEnabled = true;
            }
            else
            {
                PagerNextButton.IsEnabled = false;
            }

            if (pager.CurrentPage <= pager.TotalPages - 1 && pager.CurrentPage >= 0)
            {
                PagerPreviousButton.IsEnabled = true;
            }
            if (pager.CurrentPage < 1)
            {
                PagerPreviousButton.IsEnabled = false;
            }

            // Disable the pager buttons if there is only one page
            if (pager.CurrentPage == 0 & pager.TotalPages == 1)
            {
                PagerNextButton.IsEnabled = false;
                PagerPreviousButton.IsEnabled = false;
            }

            // Update the textbox
            PagerTextBox.Text = string.Format("Page {0} of {1} (Total Parameters: {2})", pager.CurrentPage + 1, pager.TotalPages, pager.TotalItems);

            // Set the object from the updated Pager object
            Pagination = pager;
        }

        /// <summary>
        /// Method to execute when the Upload button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BttnUpload_Click(object sender, RoutedEventArgs e)
        {
            // Generate an ID for the batch upload
            var batchId = Guid.NewGuid().ToString();

            var introTable = string.Empty;
            var metaDataTable = string.Empty;
            var groupTable = string.Empty;
            var parameterTable = string.Empty;

            var parameters = new List<SharedParameter>();

            DataTable datatable = new DataTable();

            // Read the text file and split the tables based on Revit's shared parameter file format
            try
            {
                using (StreamReader streamReader = new StreamReader(TxtBoxSpPath.Text))
                {
                    var text = streamReader.ReadToEnd();
                    var tables = text.Split('*');
                    char[] delimiter = new char[] { '\t' };

                    // Store parsed data in strings
                    introTable = tables[0];
                    metaDataTable = tables[1];
                    groupTable = tables[2];
                    parameterTable = tables[3];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            // Parse the parameters string and cast each line to SharedParameter class
            using (StringReader stringReader = new StringReader(parameterTable))
            {
                string line = string.Empty;
                string headerLine = stringReader.ReadLine();
                do
                {
                    line = stringReader.ReadLine();
                    if (line != null)
                    {
                        // Cast tab delimited line from shared parameter text file to SharedParameter object
                        var newParameter = SharedParameter.FromTxt(line);

                        // Get the name of the group and assign this to the property rather than the ID 
                        // This name will be passed to the Create() method to add as the tag
                        var groupName = Group.GetNameFromTable(groupTable, newParameter.Group);
                        newParameter.Group = groupName;

                        // Check if the parameter exists
                        if (SharedParameter.HasExactMatch(Definery, newParameter))
                        {
                            // Do nothing for now
                            // TODO: Add existing SharedParameters to a log or report of some kind.
                            Debug.WriteLine(newParameter.Name + " exists. Skipping");
                        }
                        else
                        {
                            newParameter.BatchId = batchId;

                            // Instantiate the selected item as a Collection
                            var sollection = BatchUploadCollectionCombo.SelectedItem as Collection;

                            // Create the SharedParameter
                            var response = SharedParameter.Create(Definery, newParameter, sollection.Id);

                            Debug.WriteLine(response);
                        }
                    }

                } while (line != null);
            }

            // Hide the UI
            BatchUploadGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;

            // Reload the data
            // Get the parameters of the logged in user by default and display in the DataGrid
            Definery.Parameters = SharedParameter.ByUser(
                Definery, Definery.CurrentUser.Name, Pagination.ItemsPerPage, Pagination.Offset, true
                );

            // Update the GUI anytime data is loaded
            RefreshUi();
        }

        private void UpdateUiWhenSelected()
        {
            // Enable the Add to Collection button
            AddToCollectionButton.IsEnabled = true;
        }

        /// <summary>
        /// Method to execute when the Login button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text;
            var password = PasswordPasswordBox.Password;

            var loginResponse = Definery.Authenticate(Definery, username, password);

            // If the CSRF token was retrieved from Drupal
            if (!string.IsNullOrEmpty(Definery.CsrfToken))
            {
                // Store the auth code for GET requests
                Definery.AuthCode = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(username + ":" + password));

                // Hide login form
                OverlayGrid.Visibility = Visibility.Hidden;
                LoginGrid.Visibility = Visibility.Hidden;
            }
            
            // Load all of the things!!!
            LoadData();
        }

        /// <summary>
        /// Method to execute when the Refresh button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset page back to 1
            Pagination.CurrentPage = 0;

            // Upate the pager data and UI
            UpdatePager(Pagination, 0);

            // Load all of the things!!!
            LoadData();
        }

        /// <summary>
        /// Method to execute when the Next button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PagerNextButton_Click(object sender, RoutedEventArgs e)
        {
            // Upate the pager data and UI
            UpdatePager(Pagination, 1);

            if (CollectionsList.SelectedItems.Count > 0)
            {
                // Load the data based on selected Collection and display in the DataGrid
                Definery.Parameters = SharedParameter.ByCollection(
                    Definery, CollectionsList.SelectedItem as Collection, Pagination.ItemsPerPage, Pagination.Offset, false
                    );
                DataGridParameters.ItemsSource = Definery.Parameters;
            }
            else
            {
                // Load the data based on the logged in user and display in the DataGrid
                Definery.Parameters = SharedParameter.ByUser(
                    Definery, Definery.CurrentUser.Name, Pagination.ItemsPerPage, Pagination.Offset, false
                    );
                DataGridParameters.ItemsSource = Definery.Parameters;
            }
        }

        /// <summary>
        /// Method to execute when the Previous button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PagerPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            // Upate the pager data and UI
            UpdatePager(Pagination, -1);

            if (CollectionsList.SelectedItems.Count > 0)
            {
                // Load the data based on selected Collection and display in the DataGrid
                Definery.Parameters = SharedParameter.ByCollection(
                    Definery, CollectionsList.SelectedItem as Collection, Pagination.ItemsPerPage, Pagination.Offset, false
                    );
                DataGridParameters.ItemsSource = Definery.Parameters;
            }
            else
            {
                // Load the data based on the logged in user and display in the DataGrid
                Definery.Parameters = SharedParameter.ByUser(
                    Definery, Definery.CurrentUser.Name, Pagination.ItemsPerPage, Pagination.Offset, false
                    );
                DataGridParameters.ItemsSource = Definery.Parameters;
            }
        }

        /// <summary>
        ///  Method to execute when the Batch Upload button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BatchUploadOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            // Populate the Collections combo
            BatchUploadCollectionCombo.ItemsSource = Definery.Collections;
            BatchUploadCollectionCombo.DisplayMemberPath = "Name";
            BatchUploadCollectionCombo.SelectedIndex = 0;

            // Show the batch upload form
            OverlayGrid.Visibility = Visibility.Visible;
            BatchUploadGrid.Visibility = Visibility.Visible;
        }

        private void BatchUploadCancel_Click(object sender, RoutedEventArgs e)
        {
            // Hide the batch upload form
            OverlayGrid.Visibility = Visibility.Hidden;
            BatchUploadGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the Add to Collection button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddToCollectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if the current user has any Collections first
            if (Definery.Collections.Count < 1)
            {
                MessageBox.Show("You do not have any Collections yet. Once you have created a collection, you may add Shared Parameters to it.");
            }
            else
            {
                AddToCollectionCombo.DisplayMemberPath = "Name";  // Displays the Collection name rather than object in the Add to Collections combobox
                AddToCollectionCombo.SelectedIndex = 0;  // Always select the default item so it cannot be left blank

                if (DataGridParameters.SelectedItems.Count > 0)
                {
                    // Add the Collections from the main Definery object
                    AddToCollectionCombo.ItemsSource = Definery.Collections;
                    AddToCollectionCombo.SelectedIndex = 0;

                    // Show the Add To Collection form
                    OverlayGrid.Visibility = Visibility.Visible;
                    AddToCollectionGrid.Visibility = Visibility.Visible;
                }
                else
                {
                    MessageBox.Show("Nothing is selected. Selected a Shared Parameter to add to a Collection.");
                }
            }
        }

        /// <summary>
        /// Helper method to catch when a selection changes in the DataGrid.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DataGridParameters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUiWhenSelected();
        }

        /// <summary>
        /// Helper method to refresh all UI elements after a new payload.
        /// </summary>
        private void RefreshUi()
        {
            DataGridParameters.ItemsSource = Definery.Parameters;

            // Update the pager UI elements
            //Pagination = Pager.LoadData(Definery, Pagination.ItemsPerPage, Pagination.Offset);
            UpdatePager(Pagination, 0);
        }

        /// <summary>
        /// Method to execute when the Save button is clicked on the Add to Collection form.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddToCollectionFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the selected Collection as a Collection object
            var selectedCollection = AddToCollectionCombo.SelectedItem as Collection;

            foreach (var i in DataGridParameters.SelectedItems)
            {
                // Get current Shared Parameter as a SharedParameter object
                var selectedParam = i as SharedParameter;

                // Add the Shared Parameter to the collection
                SharedParameter.AddToCollection(Definery, selectedParam, selectedCollection);
            }

            // Notify the user of the update
            MessageBox.Show("Added " + DataGridParameters.SelectedItems.Count + " parameters to " + selectedCollection.Name + ".");

            // Hide the overlay
            AddToCollectionGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;

        }

        /// <summary>
        /// Method to execute when the Cancel button is click on the Add to Collection form.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelAddToCollectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide the Add To Collection form
            OverlayGrid.Visibility = Visibility.Hidden;
            AddToCollectionGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the Add Parameter button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewParameterButton_Click(object sender, RoutedEventArgs e)
        {
            // Pass the Collections list to the combobox and configure
            NewParamFormCombo.ItemsSource = Definery.Collections;
            NewParamFormCombo.DisplayMemberPath = "Name";  // Displays the Collection name rather than object in the combobox
            NewParamFormCombo.SelectedIndex = 0;  // Always select the default item so it cannot be left blank

            // Generate a GUID by default
            NewParamGuidTextBox.Text = Guid.NewGuid().ToString();

            // Pass the DataType list to the combobox and configure
            NewParamDataTypeCombo.ItemsSource = Definery.DataTypes;
            NewParamDataTypeCombo.DisplayMemberPath = "Name";  // Displays the name rather than object in the combobox
            NewParamDataTypeCombo.SelectedIndex = 0;  // Always select the default item so it cannot be left blank

            // Clear all values
            NewParamNameTextBox.Text = "";
            NewParamDescTextBox.Text = "";
            NewParamVisibleCheck.IsChecked = true;
            NewParamUserModCheckbox.IsChecked = true;

            // Show the Add Parameter form
            OverlayGrid.Visibility = Visibility.Visible;
            NewParameterGrid.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Method to execute when the New Parameter form Cancel button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelNewParamButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide the overlay and form
            NewParameterGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the Add Parameter button on form is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewParamFormButton_Click(object sender, RoutedEventArgs e)
        {
            // Instantiate the data from the form inputs
            var collection = NewParamFormCombo.SelectedItem as Collection;

            var dataType = NewParamDataTypeCombo.SelectedItem as DataType;

            var param = new SharedParameter();
            param.Name = NewParamNameTextBox.Text;
            param.Guid = new Guid(NewParamGuidTextBox.Text);
            param.Description = NewParamDescTextBox.Text;
            param.DataType = dataType.Name;
            param.Visible = (NewParamVisibleCheck.IsChecked ?? false) ? "1" : "0";  // Reports out a 1 or 0 as a string
            param.UserModifiable = (NewParamUserModCheckbox.IsChecked ?? false) ? "1" : "0";

            var response = SharedParameter.Create(Definery, param, collection.Id);

            Debug.Write(response);

            // Hide the overlay and form
            NewParameterGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;

            MessageBox.Show("The parameter has been successfully created.");
        }

        /// <summary>
        /// Method to execute when the Add to Collection Cancel button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CancelAddToCollectionButton_Click_1(object sender, RoutedEventArgs e)
        {
            // Hide the overlay
            AddToCollectionGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the New Collection button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewCollectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the combobox in case it was previously canceled
            NewCollectionFormTextBox.Text = string.Empty;

            // Show the overlay
            NewCollectionGrid.Visibility = Visibility.Visible;
            OverlayGrid.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Method to execute when the New Collection Cancel button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewCollectionFormCancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide the overlay
            NewCollectionGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the New Collection Save button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NewCollectionFormSaveButton_Click(object sender, RoutedEventArgs e)
        {
            var response = Collection.Create(Definery, NewCollectionFormTextBox.Text, NewCollectionFormDesc.Text);

            // If the Collection was successfully created, refresh the Collections list
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                MessageBox.Show("The collection was successfully created.");

                Definery.Collections = Collection.GetAll(Definery);

                // Refresh the sidebar list UI
                CollectionsList.ItemsSource = Definery.Collections;
            }

            // Hide the overlay
            NewCollectionGrid.Visibility = Visibility.Hidden;
            OverlayGrid.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Method to execute when the My Collections selection changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CollectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Instantiate the selected item as a Collection object
            var sollection = CollectionsList.SelectedItem as Collection;

            // Get the parameters
            Definery.Parameters = SharedParameter.ByCollection(Definery, sollection, Pagination.ItemsPerPage, 0, true
                );

            // Force the pager to page 0 and update
            Pagination.CurrentPage = 0;
            UpdatePager(Pagination, 0);

            // Update the GUI anytime data is loaded
            RefreshUi();
        }
    }
}
