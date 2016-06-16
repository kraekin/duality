﻿using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using NUnit.Framework;

namespace Duality.Editor.Tests.PluginManager
{
	[TestFixture]
	public class EditorPluginManagerTest
	{
		[Test] public void PluginLoaderInitTerminate()
		{
			MockPluginLoader pluginLoader = new MockPluginLoader();
			EditorPluginManager pluginManager = new EditorPluginManager();

			// We expect the plugin manager not to assume ownership of
			// the plugin loader, e.g. not to initialize or terminate it.

			Assert.IsFalse(pluginLoader.Initialized);
			Assert.IsFalse(pluginLoader.Disposed);

			pluginManager.Init(pluginLoader);

			Assert.IsFalse(pluginLoader.Initialized);
			Assert.IsFalse(pluginLoader.Disposed);

			pluginManager.Terminate();

			Assert.IsFalse(pluginLoader.Initialized);
			Assert.IsFalse(pluginLoader.Disposed);
		}
		[Test] public void LoadPlugins()
		{
			using (MockPluginLoader pluginLoader = new MockPluginLoader())
			{
				// Set up some mock data for available assemblies
				MockAssembly[] mockPlugins = new MockAssembly[]
				{
					new MockAssembly("MockDir/MockPluginA.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginB.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir2/MockPluginC.editor.dll", typeof(MockEditorPlugin))
				};
				MockAssembly[] mockNoise = new MockAssembly[]
				{
					new MockAssembly("MockDir/MockAuxillaryA.dll"),
					new MockAssembly("MockDir/MockPluginD.core.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginE.editor.dll"),
					new MockAssembly("MockDir2/MockAuxillaryB.dll", typeof(MockEditorPlugin)),
					MockAssembly.CreateInvalid("MockDir2/MockPluginF.editor.dll"),
					MockAssembly.CreateInvalid("MockDir2/MockAuxillaryC.dll")
				};
				string[] mockLoadedPaths = new string[] { 
					mockPlugins[0].Location, mockPlugins[1].Location, mockPlugins[2].Location, 
					mockNoise[2].Location, mockNoise[4].Location };

				pluginLoader.AddBaseDir("MockDir");
				pluginLoader.AddBaseDir("MockDir2");
				for (int i = 0; i < mockPlugins.Length; i++)
				{
					pluginLoader.AddPlugin(mockPlugins[i]);
				}
				for (int i = 0; i < mockNoise.Length; i++)
				{
					pluginLoader.AddPlugin(mockNoise[i]);
				}
				pluginLoader.AddIncompatibleDll("MockDir2/MockAuxillaryD.dll");

				// Set up a plugin manager using the mock loader
				EditorPluginManager pluginManager = new EditorPluginManager();
				pluginManager.Init(pluginLoader);

				// Load all plugins
				pluginManager.LoadPlugins();
				EditorPlugin[] loadedPlugins = pluginManager.LoadedPlugins.ToArray();

				// Assert that we loaded all expected plugins, but nothing more
				Assert.AreEqual(3, loadedPlugins.Length);
				CollectionAssert.AreEquivalent(mockPlugins, loadedPlugins.Select(plugin => plugin.PluginAssembly));

				// Assert that we properly assigned all plugin properties
				Assert.IsTrue(loadedPlugins.All(plugin => plugin.AssemblyName == plugin.PluginAssembly.GetShortAssemblyName()));

				// Assert that we loaded core plugin and auxilliary libraries, but not editor plugins
				CollectionAssert.AreEquivalent(
					mockLoadedPaths, 
					pluginLoader.LoadedAssemblies);

				// Assert that we can access all assemblies and types from plugins
				foreach (MockAssembly mockAssembly in mockPlugins)
				{
					CollectionAssert.Contains(pluginManager.GetEditorAssemblies(), mockAssembly);
				}
				CollectionAssert.Contains(pluginManager.GetEditorTypes(typeof(object)), typeof(MockEditorPlugin));
				Assert.AreEqual(3, pluginManager.GetEditorTypes(typeof(MockEditorPlugin)).Count());

				pluginManager.Terminate();
			}
		}
		[Test] public void ResolveAssembly()
		{
			using (MockPluginLoader pluginLoader = new MockPluginLoader())
			{
				MockAssembly[] mockAssemblies = new MockAssembly[]
				{
					new MockAssembly("MockDir/MockPluginA.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginB.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginC.editor.dll", typeof(MockEditorPlugin))
				};
				MockAssembly mockCoreAssembly = new MockAssembly("MockDir/MockPluginD.core.dll");

				// Set up some mock data for available assemblies
				pluginLoader.AddBaseDir("MockDir");
				for (int i = 0; i < mockAssemblies.Length; i++)
				{
					pluginLoader.AddPlugin(mockAssemblies[i]);
				}
				pluginLoader.AddPlugin(mockCoreAssembly);

				// Set up a plugin manager using the mock loader
				EditorPluginManager pluginManager = new EditorPluginManager();
				pluginManager.Init(pluginLoader);

				{
					// First, make sure the attempt to resolve a not-yet-loaded plugin
					// will result in loading it immediately to satisfy dependency relations
					Assembly resolvedAssembly = pluginLoader.InvokeResolveAssembly(mockAssemblies[0].FullName);

					// Assert that we successfully resolved it with a plugin (not just an assembly)
					Assert.IsNotNull(resolvedAssembly);
					Assert.AreSame(mockAssemblies[0], resolvedAssembly);
					Assert.AreEqual(1, pluginManager.LoadedPlugins.Count());
					Assert.AreSame(mockAssemblies[0], pluginManager.LoadedPlugins.First().PluginAssembly);
					Assert.AreEqual(1, pluginLoader.LoadedAssemblies.Count());
					CollectionAssert.Contains(pluginLoader.LoadedAssemblies, mockAssemblies[0].Location);
				}

				{
					// Attempt to resolve a not-yet-loaded core plugin
					Assembly resolvedAssembly = pluginLoader.InvokeResolveAssembly(mockCoreAssembly.FullName);

					// Assert that we did not resolve this, nor load any assemblies.
					// Leave this to the CorePluginManager, which can properly load them as a plugin.
					Assert.IsNull(resolvedAssembly);
					Assert.AreEqual(1, pluginManager.LoadedPlugins.Count());
					Assert.AreEqual(1, pluginLoader.LoadedAssemblies.Count());
				}

				// Load and init all plugins
				pluginManager.LoadPlugins();
				pluginManager.InitPlugins();

				// Assert that we do not have any duplicates and still the same count of plugins
				Assert.AreEqual(3, pluginManager.LoadedPlugins.Count());

				// Assert that other resolve calls will map to existing assemblies,
				// both for plugins and auxilliary libraries
				for (int i = 0; i < mockAssemblies.Length; i++)
				{
					Assert.AreSame(mockAssemblies[i], pluginLoader.InvokeResolveAssembly(mockAssemblies[i].FullName));
				}
				
				// Assert that we do not have any duplicates and still the same count of plugins
				Assert.AreEqual(3, pluginManager.LoadedPlugins.Count());

				pluginManager.Terminate();
			}
		}
		[Test] public void DuplicateLoad()
		{
			using (MockPluginLoader pluginLoader = new MockPluginLoader())
			{
				MockAssembly[] mockPlugins = new MockAssembly[]
				{
					new MockAssembly("MockDir/MockPluginA.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginB.editor.dll", typeof(MockEditorPlugin)),
					new MockAssembly("MockDir/MockPluginC.editor.dll", typeof(MockEditorPlugin))
				};
				MockAssembly[] mockAssemblies = new MockAssembly[]
				{
					mockPlugins[0],
					mockPlugins[1],
					mockPlugins[2],
					new MockAssembly("MockDir/MockAuxilliaryA.dll"),
					new MockAssembly("MockDir/MockAuxilliaryB.dll")
				};

				// Set up some mock data for available assemblies
				pluginLoader.AddBaseDir("MockDir");
				for (int i = 0; i < mockAssemblies.Length; i++)
				{
					pluginLoader.AddPlugin(mockAssemblies[i]);
				}

				// Set up a plugin manager using the mock loader
				EditorPluginManager pluginManager = new EditorPluginManager();
				pluginManager.Init(pluginLoader);

				// Load and init all plugins
				pluginManager.LoadPlugins();
				pluginManager.InitPlugins();

				// Now load them again
				pluginManager.LoadPlugins();

				// Assert that we do not have any duplicates and still the same count of plugins
				Assert.AreEqual(3, pluginManager.LoadedPlugins.Count());

				// Let's try loading assembly duplicates manually
				for (int i = 0; i < mockPlugins.Length; i++)
				{
					EditorPlugin plugin = pluginManager.LoadPlugin(mockAssemblies[i], mockAssemblies[i].Location);
					Assert.IsNotNull(plugin);
				}

				// Assert that we do not have any duplicates and no disposed plugins
				Assert.AreEqual(3, pluginManager.LoadedPlugins.Count());

				pluginManager.Terminate();
			}
		}
	}
}