﻿namespace MiniOrchard.Data
{
	using System;
	using MiniOrchard;
	using MiniOrchard.Data.Providers;
	using MiniOrchard.FileSystems.AppData;
	using MiniOrchard.Logging;
	using MiniOrchard.Setting;
	using NHibernate;
	using NHibernate.Cfg;

	public interface ISessionFactoryHolder : ISingletonDependency
	{
		ISessionFactory GetSessionFactory();
		Configuration GetConfiguration();
		SessionFactoryParameters GetSessionFactoryParameters();
	}

	public class SessionFactoryHolder : ISessionFactoryHolder, IDisposable
	{
		private readonly ShellSettings _shellSettings;
		private readonly ShellBlueprint _shellBlueprint;
		private readonly IDataServicesProviderFactory _dataServicesProviderFactory;
		private readonly IAppDataFolder _appDataFolder;
		private readonly ISessionConfigurationCache _sessionConfigurationCache;

		private ISessionFactory _sessionFactory;
		private Configuration _configuration;

		public SessionFactoryHolder(
			ShellSettings shellSettings,
			ShellBlueprint shellBlueprint,
			IDataServicesProviderFactory dataServicesProviderFactory,
			IAppDataFolder appDataFolder,
			ISessionConfigurationCache sessionConfigurationCache)
		{
			_shellSettings = shellSettings;
			_shellBlueprint = shellBlueprint;
			_dataServicesProviderFactory = dataServicesProviderFactory;
			_appDataFolder = appDataFolder;
			_sessionConfigurationCache = sessionConfigurationCache;

			Logger = NullLogger.Instance;
		}

		public ILogger Logger { get; set; }

		public void Dispose()
		{
			if (_sessionFactory != null)
			{
				_sessionFactory.Dispose();
				_sessionFactory = null;
			}
		}

		public ISessionFactory GetSessionFactory()
		{
			lock (this)
			{
				if (_sessionFactory == null)
				{
					_sessionFactory = BuildSessionFactory();
				}
			}
			return _sessionFactory;
		}

		public Configuration GetConfiguration()
		{
			lock (this)
			{
				if (_configuration == null)
				{
					_configuration = BuildConfiguration();
				}
			}
			return _configuration;
		}

		private ISessionFactory BuildSessionFactory()
		{
			Logger.Debug("Building session factory");

			//if (!_hostEnvironment.IsFullTrust)
			//    NHibernate.Cfg.Environment.UseReflectionOptimizer = false;

			Configuration config = GetConfiguration();
			var result = config.BuildSessionFactory();
			Logger.Debug("Done building session factory");
			return result;
		}

		private Configuration BuildConfiguration()
		{
			Logger.Debug("Building configuration");
			var parameters = GetSessionFactoryParameters();

			var config = _sessionConfigurationCache.GetConfiguration(() =>
				_dataServicesProviderFactory
					.CreateProvider(parameters)
					.BuildConfiguration(parameters));

			#region NH-2.1.2 specific optimization
			// cannot be done in fluent config
			// the IsSelectable = false prevents unused ContentPartRecord proxies from being created 
			// for each ContentItemRecord or ContentItemVersionRecord.
			// done for perf reasons - has no other side-effect

			foreach (var persistentClass in config.ClassMappings)
			{
				if (persistentClass.EntityName.StartsWith("Orchard.ContentManagement.Records."))
				{
					foreach (var property in persistentClass.PropertyIterator)
					{
						if (property.Name.EndsWith("Record") && !property.IsBasicPropertyAccessor)
						{
							property.IsSelectable = false;
						}
					}
				}
			}
			#endregion

			Logger.Debug("Done Building configuration");
			return config;
		}

		public SessionFactoryParameters GetSessionFactoryParameters()
		{
			var shellPath = _appDataFolder.Combine("Sites", _shellSettings.Name);
			_appDataFolder.CreateDirectory(shellPath);

			var shellFolder = _appDataFolder.MapPath(shellPath);

			return new SessionFactoryParameters
			{
				Provider = _shellSettings.DataProvider,
				DataFolder = shellFolder,
				ConnectionString = _shellSettings.DataConnectionString,
				RecordDescriptors = _shellBlueprint.Records,
			};
		}
	}
}
