﻿// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.Python;
using Microsoft.CookiecutterTools;
using System.Collections.ObjectModel;
using Microsoft.CookiecutterTools.Model;
using Microsoft.CookiecutterTools.ViewModel;

namespace CookiecutterTests {
    [TestClass]
    public class CookiecutterIntegrationTests {
        private const string GitHubTemplatePath = "https://github.com/audreyr/Cookiecutter-pypackage";
        private const string NoUserConfigFilePath = "";

        private const string OnlineNotTemplateUrl = "https://github.com/Microsoft/PTVS";
        private const string OnlineNonExistingUrl = "https://github.com/Microsoft/---";
        private const string OnlineTemplateUrl = "https://github.com/brettcannon/python-azure-web-app-cookiecutter";
        private const string OnlineTemplateRepoFullName = "brettcannon/python-azure-web-app-cookiecutter";
        private const string OnlineTemplateRepoOwner = "brettcannon";
        private const string OnlineTemplateRepoName = "python-azure-web-app-cookiecutter";

        private static string NonExistingLocalTemplatePath => Path.Combine(TestData.GetPath("TestData"), "Cookiecutter", "notemplate");
        private static string TestLocalTemplatePath => Path.Combine(TestData.GetPath("TestData"), "Cookiecutter", "template");
        private static string TestInstalledTemplateFolderPath => Path.Combine(TestData.GetPath("TestData"), "Cookiecutter", "installed");
        private static string TestUserConfigFilePath => Path.Combine(TestData.GetPath("TestData"), "Cookiecutter", "userconfig.yaml");
        private static string TestFeedPath => Path.Combine(TestData.GetPath("TestData"), "Cookiecutter", "feed.txt");

        private MockRedirector _redirector;
        private IGitClient _gitClient;
        private IGitHubClient _gitHubClient;
        private ICookiecutterClient _cutterClient;
        private CookiecutterViewModel _vm;
        private ITemplateSource _installedTemplateSource;
        private ITemplateSource _gitHubTemplateSource;
        private ITemplateSource _feedTemplateSource;

        internal static ContextItemViewModel[] LocalTemplateWithUserConfigContextItems { get; } = new ContextItemViewModel[] {
                new ContextItemViewModel("full_name", "Configured User"),
                new ContextItemViewModel("email", "configured@email"),
                new ContextItemViewModel("github_username", "configuredgithubuser"),
                new ContextItemViewModel("project_name", "Default Project Name"),
                new ContextItemViewModel("project_slug", "{{ cookiecutter.project_name.lower().replace(' ', '_') }}"),
                new ContextItemViewModel("pypi_username", "{{ cookiecutter.github_username }}"),
                new ContextItemViewModel("version", "0.1.0"),
                new ContextItemViewModel("use_azure", "y"),
                new ContextItemViewModel("open_source_license", "BSD license", new string[] { "MIT license", "BSD license", "ISC license", "Apache Software License 2.0", "GNU General Public License v3", "Not open source" }),
                new ContextItemViewModel("port", "5000"),
                // Note that _copy_without_render item should not appear
        };

        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            PythonTestData.Deploy();
        }

        [TestInitialize]
        public void SetupTest() {
            _redirector = new MockRedirector();

            var output = TestData.GetTempPath("Cookiecutter", true);
            var outputProjectFolder = Path.Combine(output, "integration");
            var feedUrl = new Uri(TestFeedPath);
            var installedPath = TestInstalledTemplateFolderPath;
            var userConfigFilePath = TestUserConfigFilePath;

            _gitClient = new GitClient(GitClient.RecommendedGitFilePath);
            _gitHubClient = new GitHubClient();
            _cutterClient = new CookiecutterClientProvider().Create(false);
            _installedTemplateSource = new LocalTemplateSource(installedPath, _gitClient);
            _gitHubTemplateSource = new GitHubTemplateSource(_gitHubClient);
            _feedTemplateSource = new FeedTemplateSource(feedUrl);

            _vm = new CookiecutterViewModel(_cutterClient, _gitHubClient, _gitClient, _redirector, _installedTemplateSource, _feedTemplateSource, _gitHubTemplateSource, null);
            _vm.UserConfigFilePath = userConfigFilePath;
            _vm.OutputFolderPath = outputProjectFolder;
        }

        [TestMethod]
        public async Task Search() {
            await _vm.SearchAsync();

            Assert.AreEqual(1, _vm.Installed.Templates.Count);
            Assert.AreEqual(6, _vm.Recommended.Templates.Count);

            Assert.AreEqual(27, _vm.GitHub.Templates.Count);
            Assert.AreEqual(26, _vm.GitHub.Templates.OfType<TemplateViewModel>().Count());
            Assert.AreEqual(1, _vm.GitHub.Templates.OfType<ContinuationViewModel>().Count());

            var continuationVM = _vm.GitHub.Templates.Last() as ContinuationViewModel;
            Assert.IsNotNull(continuationVM);
            Assert.IsFalse(string.IsNullOrEmpty(continuationVM.ContinuationToken));

            await _vm.LoadMoreTemplates(continuationVM.ContinuationToken);
            Assert.AreEqual(53, _vm.GitHub.Templates.Count);
            Assert.AreEqual(52, _vm.GitHub.Templates.OfType<TemplateViewModel>().Count());
            Assert.AreEqual(1, _vm.GitHub.Templates.OfType<ContinuationViewModel>().Count());

            // The old "Load more" will be removed, but another one will be added after the new batch of results
            CollectionAssert.DoesNotContain(_vm.GitHub.Templates, continuationVM);
            continuationVM = _vm.GitHub.Templates.Last() as ContinuationViewModel;
            Assert.IsNotNull(continuationVM);
        }

        [TestMethod]
        public async Task SearchLocalTemplate() {
            _vm.SearchTerm = TestLocalTemplatePath;
            await _vm.SearchAsync();

            Assert.AreEqual(0, _vm.Installed.Templates.Count);
            Assert.AreEqual(0, _vm.GitHub.Templates.Count);
            Assert.AreEqual(0, _vm.Recommended.Templates.Count);
            Assert.AreEqual(1, _vm.Custom.Templates.Count);

            var template = _vm.Custom.Templates[0] as TemplateViewModel;
            PrintTemplate(template);

            Assert.AreEqual(TestLocalTemplatePath, template.DisplayName);
            Assert.AreEqual(TestLocalTemplatePath, template.ClonedPath);
            Assert.IsTrue(string.IsNullOrEmpty(template.Description));
            Assert.IsTrue(string.IsNullOrEmpty(template.RemoteUrl));
        }

        [TestMethod]
        public async Task SearchNonExistingLocalTemplate() {
            _vm.SearchTerm = NonExistingLocalTemplatePath;
            await _vm.SearchAsync();

            Assert.AreEqual(0, _vm.Installed.Templates.Count);
            Assert.AreEqual(0, _vm.GitHub.Templates.Count);
            Assert.AreEqual(0, _vm.Recommended.Templates.Count);
            Assert.AreEqual(0, _vm.Custom.Templates.Count);
        }

        [TestMethod]
        public async Task SearchOnlineTemplate() {
            _vm.SearchTerm = OnlineTemplateUrl;
            await _vm.SearchAsync();

            Assert.AreEqual(0, _vm.Installed.Templates.Count);
            Assert.AreEqual(0, _vm.GitHub.Templates.Count);
            Assert.AreEqual(0, _vm.Recommended.Templates.Count);
            Assert.AreEqual(1, _vm.Custom.Templates.Count);

            var template = _vm.Custom.Templates[0] as TemplateViewModel;
            PrintTemplate(template);

            Assert.AreEqual(OnlineTemplateUrl, template.DisplayName);
            Assert.AreEqual(OnlineTemplateRepoOwner, template.RepositoryOwner);
            Assert.AreEqual(OnlineTemplateRepoName, template.RepositoryName);
            Assert.AreEqual(OnlineTemplateRepoFullName, template.RepositoryFullName);
            Assert.AreEqual(OnlineTemplateUrl, template.RemoteUrl);
            Assert.IsTrue(string.IsNullOrEmpty(template.Description));
            Assert.IsTrue(string.IsNullOrEmpty(template.ClonedPath));
        }

        [TestMethod]
        public async Task SearchOnlineNotTemplate() {
            // Right now, we don't validate that it's a cookiecutter template for user entered url
            _vm.SearchTerm = OnlineNotTemplateUrl;
            await _vm.SearchAsync();

            Assert.AreEqual(0, _vm.Installed.Templates.Count);
            Assert.AreEqual(0, _vm.GitHub.Templates.Count);
            Assert.AreEqual(0, _vm.Recommended.Templates.Count);
            Assert.AreEqual(1, _vm.Custom.Templates.Count);
        }

        [TestMethod]
        public async Task SearchOnlineNonExistingUrl() {
            // Right now, we don't validate that it's an existing url
            _vm.SearchTerm = OnlineNonExistingUrl;
            await _vm.SearchAsync();

            Assert.AreEqual(0, _vm.Installed.Templates.Count);
            Assert.AreEqual(0, _vm.GitHub.Templates.Count);
            Assert.AreEqual(0, _vm.Recommended.Templates.Count);
            Assert.AreEqual(1, _vm.Custom.Templates.Count);
        }

        [TestMethod]
        public async Task CreateFromLocalTemplate() {
            _vm.SearchTerm = TestLocalTemplatePath;
            await _vm.SearchAsync();

            var template = _vm.Custom.Templates[0] as TemplateViewModel;
            await _vm.SelectTemplate(template);

            await _vm.LoadTemplateAsync();

            // Local template doesn't need to be cloned
            Assert.IsFalse(_vm.IsCloning);
            Assert.IsFalse(_vm.IsCloningError);
            Assert.IsFalse(_vm.IsCloningSuccess);

            PrintContextItems(_vm.ContextItems);
            CollectionAssert.AreEqual(LocalTemplateWithUserConfigContextItems, _vm.ContextItems, new ContextItemViewModelComparer());

            _vm.ContextItems.Single(item => item.Name == "full_name").Val = "Integration Test User";
            _vm.ContextItems.Single(item => item.Name == "open_source_license").Val = "Apache Software License 2.0";
            _vm.OutputFolderPath = Path.Combine(_vm.OutputFolderPath, "LocalTemplate");

            await _vm.CreateFilesAsync();

            Assert.IsFalse(_vm.IsCreating);
            Assert.IsTrue(_vm.IsCreatingSuccess);
            Assert.IsFalse(_vm.IsCreatingError);

            var reportFilePath = Path.Combine(_vm.OutputFolderPath, "report.txt");
            Assert.IsTrue(File.Exists(reportFilePath), "Failed to generate some project files.");
            var report = CookiecutterClientTests.ReadReport(reportFilePath);

            var expected = new Dictionary<string, string>() {
                { "full_name", "Integration Test User" },
                { "email", "configured@email" },
                { "github_username", "configuredgithubuser" },
                { "project_name", "Default Project Name" },
                { "project_slug", "default_project_name" },
                { "pypi_username", "configuredgithubuser" },
                { "version", "0.1.0" },
                { "use_azure", "y" },
                { "open_source_license", "Apache Software License 2.0" },
                { "port", "5000" },
            };
            CollectionAssert.AreEqual(expected, report);
        }

        [TestMethod]
        public async Task CreateFromOnlineTemplate() {
            _vm.SearchTerm = OnlineTemplateUrl;
            await _vm.SearchAsync();

            var template = _vm.Custom.Templates[0] as TemplateViewModel;
            await _vm.SelectTemplate(template);

            await _vm.LoadTemplateAsync();

            // Local template needs to be cloned
            Assert.IsFalse(_vm.IsCloning);
            Assert.IsFalse(_vm.IsCloningError);
            Assert.IsTrue(_vm.IsCloningSuccess);

            PrintContextItems(_vm.ContextItems);

            _vm.ContextItems.Single(item => item.Name == "static_assets_directory").Val = "static_files";
            _vm.OutputFolderPath = Path.Combine(_vm.OutputFolderPath, "OnlineTemplate");

            await _vm.CreateFilesAsync();

            Assert.IsFalse(_vm.IsCreating);
            Assert.IsTrue(_vm.IsCreatingSuccess);
            Assert.IsFalse(_vm.IsCreatingError);

            Assert.IsTrue(Directory.Exists(Path.Combine(_vm.OutputFolderPath, "static_files")));
            Assert.IsTrue(Directory.Exists(Path.Combine(_vm.OutputFolderPath, "post-deployment")));
            Assert.IsTrue(File.Exists(Path.Combine(_vm.OutputFolderPath, "web.config")));
            Assert.IsTrue(File.Exists(Path.Combine(_vm.OutputFolderPath, "static_files", "web.config")));
            Assert.IsTrue(File.Exists(Path.Combine(_vm.OutputFolderPath, "post-deployment", "install-requirements.ps1")));
            Assert.IsFalse(File.Exists(Path.Combine(_vm.OutputFolderPath, "install-requirements.ps1")));
        }

        private static void PrintResults(ObservableCollection<object> items) {
            foreach (var item in items) {
                var template = item as TemplateViewModel;
                if (template != null) {
                    PrintTemplate(template);
                } else {
                    Console.WriteLine(item);
                }
            }
        }

        private static void PrintContextItems(ObservableCollection<ContextItemViewModel> items) {
            Console.WriteLine("Context Items");
            foreach (var item in items) {
                Console.WriteLine($"Name: '{item.Name}', Value: '{item.Val}', Default: '{item.Default}'");
            }
        }

        private static void PrintTemplate(TemplateViewModel template) {
            Console.WriteLine($"DisplayName: '{template.DisplayName}', RemoteUrl: '{template.RemoteUrl}', ClonedPath: '{template.ClonedPath}', Desc: '{template.Description}'");
        }

        class ContextItemViewModelComparer : IComparer {
            public int Compare(object x, object y) {
                if (x == y) {
                    return 0;
                }

                var a = x as ContextItemViewModel;
                var b = y as ContextItemViewModel;

                if (a == null) {
                    return -1;
                }

                if (b == null) {
                    return -1;
                }

                int res;
                res = a.Name.CompareTo(b.Name);
                if (res != 0) {
                    return res;
                }

                res = a.Val.CompareTo(b.Val);
                if (res != 0) {
                    return res;
                }

                res = a.Default.CompareTo(b.Default);
                if (res != 0) {
                    return res;
                }

                return 0;
            }
        }
    }
}
