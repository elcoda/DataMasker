using System;
using CommandLine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataMasker.Interfaces;
using DataMasker.Models;
using Konsole;
using Newtonsoft.Json;
using DataMasker.Utils;

namespace DataMasker.Runner
{
    internal class Program
    {
        private static readonly Dictionary<ProgressType, ProgressbarUpdate> _progressBars = new Dictionary<ProgressType, ProgressbarUpdate>();

        private static Options cliOptions;

        private static void Main(
            string[] args)
        {

            Parser.Default.ParseArguments<Options>(args)
                  .WithParsed(
                       options =>
                       {
                           cliOptions = options;
                           try
                           {
                               RuntimeArgumentHandle();
                           }
                           catch (Exception ex)
                           {

                               LogMessage("[Exception ocurred] " +ex.Message);
                               Environment.Exit(1);
                           }
                       });
        }

        private static void InitProgressBars()
        {
            if (cliOptions.NoOutput)
            {
                return;
            }

            _progressBars.Add(
                ProgressType.Overall,
                new ProgressbarUpdate { ProgressBar = new ProgressBar(PbStyle.SingleLine, 0), LastMessage = "Overall Progress" });

            _progressBars.Add(
                ProgressType.Updating,
                new ProgressbarUpdate { ProgressBar = new ProgressBar(PbStyle.SingleLine, 0), LastMessage = "Update Progress" });

            _progressBars.Add(
                ProgressType.Masking,
                new ProgressbarUpdate { ProgressBar = new ProgressBar(PbStyle.SingleLine, 0), LastMessage = "Masking Progress" });
        }

        private static void UpdateProgress(
            ProgressType progressType,
            int current,
            int? max = null,
            string message = null)
        {
            if (cliOptions.NoOutput)
            {
                return;
            }

            max = max ??
                  _progressBars[progressType]
                     .ProgressBar.Max;

            _progressBars[progressType]
               .ProgressBar.Max = max.Value;

            message = message ??
                      _progressBars[progressType]
                         .LastMessage;

            _progressBars[progressType]
               .ProgressBar.Refresh(current, message);
        }
        private static void RuntimeArgumentHandle()
        {
            if (cliOptions.PrintOptions)
            {
                WriteLine();
                WriteLine(JsonConvert.SerializeObject(cliOptions, Formatting.Indented));
                WriteLine();
                return;
            }

            InitProgressBars();
            Config config = Config.Load(cliOptions.ConfigFile);
            if (cliOptions.DryRun != null)
            {
                config.DataSource.DryRun = cliOptions.DryRun.Value;
            }

            if (!string.IsNullOrEmpty(cliOptions.Locale))
            {
                config.DataGeneration.Locale = cliOptions.Locale;
            }

            if (cliOptions.UpdateBatchSize != null)
            {
                config.DataSource.UpdateBatchSize = cliOptions.UpdateBatchSize;
            }

            Execute(config);
        }

        private static void WriteLine(
            string message = null)
        {
            if (!cliOptions.NoOutput)
            {
                Console.WriteLine(message);
            }
        }

        private static void LogMessage(
           string message = null)
        {
           
           Console.WriteLine("   [DataMasker] > " + message);
           
        }



        private static void Execute(
            Config config)
        {
            LogMessage("Masking Data");
            UpdateProgress(ProgressType.Overall, 0, config.Tables.Count, "Overall Progress");

            var dataProviders = new List<IDataProvider>
      {
          new BogusDataProvider(config.DataGeneration),
          new SqlDataProvider(new System.Data.SqlClient.SqlConnection(config.DataSource.GetConnectionString()))
      };

            //create a data masker
            IDataMasker dataMasker = new DataMasker(dataProviders);

            //grab our dataSource from the config, note: you could just ignore the config.DataSource.Type
            //and initialize your own instance
            IDataSource dataSource = DataSourceProvider.Provide(config.DataSource.Type, config.DataSource);

            for (int i = 0; i < config.Tables.Count; i++)
            {
                TableConfig tableConfig = config.Tables[i];
                IEnumerable<IDictionary<string, object>> rows = null;


                var rowCount = dataSource.GetCount(tableConfig);
                UpdateProgress(ProgressType.Masking, 0, (int)rowCount, "Masking Progress");
                UpdateProgress(ProgressType.Updating, 0, (int)rowCount, "Update Progress");

                LogMessage("Starting masking processo on table "+tableConfig.Name + "...");
                rows = dataSource.GetData(tableConfig);


               
                int rowIndex = 0;

                var maskedRows = rows.Select(row =>
                {
                    rowIndex++;
                    UpdateProgress(ProgressType.Masking, rowIndex);

                    return dataMasker.Mask(row, tableConfig);
                
                });

                //update all rows
                dataSource.UpdateRows(maskedRows, rowCount, tableConfig, totalUpdated => UpdateProgress(ProgressType.Updating, totalUpdated));
                UpdateProgress(ProgressType.Overall, i + 1);
                LogMessage("Completed masking process for sql statement:" + tableConfig.Name + ".");

            }

            ISqlStatementDataSource sqlStatementDataSource = SqlStatementDataProvier.Provide(config.DataSource.Type, config.DataSource);

            if (config.SqlStatements!=null) { 
                for (int i = 0; i < config.SqlStatements.Count; i++)
                {
                    SqlStatementConfig sqlStatementConfig = config.SqlStatements[i];

                    //IDataMasker dataMasker = new DataMasker(dataProviders);
                    LogMessage("Starting masking process for sql statement: "+ sqlStatementConfig.Name+  "...");
                    var rowCount = config.SqlStatements.Count();
                    UpdateProgress(ProgressType.Masking, 0, (int)rowCount, "Masking Progress");
                    UpdateProgress(ProgressType.Updating, 0, (int)rowCount, "Update Progress");
                    sqlStatementDataSource.Execute(sqlStatementConfig);
                    LogMessage("Completed masking process for sql statement:" + sqlStatementConfig.Name +".");


                }
            }

            LogMessage("Done");
        }
    }
}
