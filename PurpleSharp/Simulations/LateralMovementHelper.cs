﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security.Principal;
using TaskScheduler;

namespace PurpleSharp.Simulations
{
    class LateralMovementHelper
    {
        // From https://stackoverflow.com/questions/23481394/programmatically-install-windows-service-on-remote-machine
        public static void CreateRemoteService(Computer computer, bool cleanup)
        {
            var scmHandle = WinAPI.OpenSCManager(computer.Fqdn, null, Structs.SCM_ACCESS.SC_MANAGER_CREATE_SERVICE);

            if (scmHandle == IntPtr.Zero)
            {
                DateTime dtime = DateTime.Now;
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine("{0}[{1}] Could not obtain a handle to SCM on {2}. Not an admin ?", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                return;

            }
            string servicePath = @"C:\Windows\Temp\superlegit.exe";      // A path to some running service now
            string serviceName = "UpdaterService";
            string serviceDispName = "Super Legit Update Service";

            IntPtr svcHandleCreated = IntPtr.Zero;
            int createdErr = 0;
            bool created = CreateService(scmHandle, servicePath, serviceName, serviceDispName, out svcHandleCreated, out createdErr);

            if (created)
            {
                DateTime dtime = DateTime.Now;
                Console.WriteLine("{0}[{1}] Successfully created a service on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);

                if (cleanup)
                {
                    IntPtr svcHandleOpened = WinAPI.OpenService(scmHandle, serviceName, Structs.SERVICE_ACCESS.SERVICE_ALL_ACCESS);
                    bool deletedService = WinAPI.DeleteService(svcHandleOpened);
                    WinAPI.CloseServiceHandle(svcHandleOpened);

                }
            }

            if (!created)
            {
                if (createdErr == 1073)
                {
                    // Error: "The specified service already exists"

                    IntPtr svcHandleOpened = WinAPI.OpenService(scmHandle, serviceName, Structs.SERVICE_ACCESS.SERVICE_ALL_ACCESS);

                    if (svcHandleOpened != IntPtr.Zero)
                    {
                        bool deletedService = WinAPI.DeleteService(svcHandleOpened);
                        WinAPI.CloseServiceHandle(svcHandleOpened);

                        if (deletedService)
                        {
                            // Try to create it again:
                            bool created2 = CreateService(scmHandle, servicePath, serviceName, serviceDispName, out svcHandleCreated, out createdErr);
                            if (created2)
                            {
                                DateTime dtime = DateTime.Now;
                                Console.WriteLine("{0}[{1}] Successfully deleted and recreated a service on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                                //throw new Win32Exception(createdErr);

                                if (cleanup)
                                {
                                    IntPtr svcHandleOpened2 = WinAPI.OpenService(scmHandle, serviceName, Structs.SERVICE_ACCESS.SERVICE_ALL_ACCESS);
                                    bool deletedService2 = WinAPI.DeleteService(svcHandleOpened2);
                                    WinAPI.CloseServiceHandle(svcHandleOpened2);

                                }

                            }
                        }
                        else
                        {
                            DateTime dtime = DateTime.Now;
                            Console.WriteLine("{0}[{1}] Failed to create service on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);

                            // Service was successfully opened, but unable to delete the service
                        }
                    }
                    else
                    {
                        // Unable to open that service name w/ All Access
                        DateTime dtime = DateTime.Now;
                        Console.WriteLine("{0}[{1}] Failed to create service on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                        int openErr = Marshal.GetLastWin32Error();
                        //throw new Win32Exception(openErr);
                    }

                }
                else
                {
                    // Some other serice creation error than it already existing
                    DateTime dtime = DateTime.Now;
                    Console.WriteLine("{0}[{1}] Failed to create service on {2}. ", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                    //throw new Win32Exception(createdErr);
                }

            }


            WinAPI.StartService(svcHandleCreated, 0, null);


            WinAPI.CloseServiceHandle(svcHandleCreated);
            WinAPI.CloseServiceHandle(scmHandle);
        }

        // From https://stackoverflow.com/questions/23481394/programmatically-install-windows-service-on-remote-machine
        static bool CreateService(IntPtr scmHandle, string servicePath, string serviceName, string serviceDispName, out IntPtr serviceHandleCreated, out int errorCodeIfFailed)
        {
            serviceHandleCreated = IntPtr.Zero;
            errorCodeIfFailed = 0;

            serviceHandleCreated = WinAPI.CreateService(
                scmHandle,
                serviceName,
                serviceDispName,
                Structs.SERVICE_ACCESS.SERVICE_ALL_ACCESS,
                Structs.SERVICE_TYPES.SERVICE_WIN32_OWN_PROCESS,
                Structs.SERVICE_START_TYPES.SERVICE_AUTO_START,
                Structs.SERVICE_ERROR_CONTROL.SERVICE_ERROR_NORMAL,
                servicePath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (serviceHandleCreated == IntPtr.Zero)
            {
                errorCodeIfFailed = Marshal.GetLastWin32Error();
            }

            return serviceHandleCreated != IntPtr.Zero;
        }

        public static void WinRMCodeExecution(Computer computer, string command)
        {
            try
            {
                var connectTo = new Uri(String.Format("http://{0}:5985/wsman", computer.Fqdn));
                var connection = new WSManConnectionInfo(connectTo);
                var runspace = RunspaceFactory.CreateRunspace(connection);
                runspace.Open();
                using (var powershell = PowerShell.Create())
                {
                    powershell.Runspace = runspace;
                    powershell.AddScript(command);
                    var results = powershell.Invoke();
                    runspace.Close();
                    DateTime dtime = DateTime.Now;
                    Console.WriteLine("{0}[{1}] Successfully created a process using WinRM on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);

                    /*
                    Console.WriteLine("Return command ");
                    foreach (var obj in results.Where(o => o != null))
                    {
                        Console.WriteLine("\t" + obj);
                    }
                    */
                }
            }
            catch (Exception ex)
            {
                DateTime dtime = DateTime.Now;
                if (ex.Message.Contains("Access is denied")) Console.WriteLine("{0}[{1}] Failed to execute execute a process using WMI on {2}. (Access Denied)", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                else if (ex.GetType().ToString().Contains("PSRemotingTransportException")) Console.WriteLine("{0}[{1}] Failed to execute execute a process using WMI on {2}. (Port Closed)", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                else Console.WriteLine("{0}[{1}] Failed to execute a process using WinRM on {2}. {3}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn, ex.GetType());

            }
            

        }

        public static void WmiCodeExecution(Computer computer, string command)
        {
            try
            {
                ConnectionOptions connectoptions = new ConnectionOptions();

                var processToRun = new[] { command };
                var wmiScope = new ManagementScope(String.Format("\\\\{0}\\root\\cimv2", computer.Fqdn), connectoptions);
                var wmiProcess = new ManagementClass(wmiScope, new ManagementPath("Win32_Process"), new ObjectGetOptions());
                wmiProcess.InvokeMethod("Create", processToRun);
                DateTime dtime = DateTime.Now;
                Console.WriteLine("{0}[{1}] Successfully created a process using WMI on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);

            }
            catch (Exception ex)
            {
                DateTime dtime = DateTime.Now;
                if (ex.Message.Contains("ACCESSDENIED")) Console.WriteLine("{0}[{1}] Failed to execute execute a process using WMI on {2}. (Access Denied)", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                else Console.WriteLine("{0}[{1}] Failed to execute a process using WMI on {2}. {3}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn, ex.GetType());
            }



        }

        public static void CreateRemoteScheduledTask(Computer computer, string command, bool cleanup)
        {
            try
            {
                /*
                ConnectionOptions connectoptions = new ConnectionOptions();
                var wmiScope = new ManagementScope(String.Format("\\\\{0}\\root\\cimv2", computer.Fqdn), connectoptions);
                wmiScope.Connect();

                //Getting time
                string serverTime = null;
                SelectQuery timeQuery = new SelectQuery(@"select LocalDateTime from Win32_OperatingSystem");
                ManagementObjectSearcher timeQuerySearcher = new ManagementObjectSearcher(timeQuery);
                foreach (ManagementObject mo in timeQuerySearcher.Get())
                {
                    serverTime = mo["LocalDateTime"].ToString();
                }

                //Adding 2 minutes to the time
                Console.WriteLine("Got Remote computer time {0}", serverTime);

                //running command
                object[] cmdParams = { command, serverTime, false, null, null, true, 0 };
                ManagementClass serverCommand = new ManagementClass(wmiScope, new ManagementPath("Win32_ScheduledJob"), null);
                serverCommand.InvokeMethod("Create", cmdParams);
                DateTime dtime = DateTime.Now;
                Console.WriteLine("{0}[{1}] Successfully created a process using WMI on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                */
                
                /*
                string strJobId = "";
                ConnectionOptions connectoptions = new ConnectionOptions();
                connectoptions.Impersonation = ImpersonationLevel.Impersonate;
                connectoptions.Authentication = AuthenticationLevel.PacketPrivacy;
                connectoptions.EnablePrivileges = true;
                Console.WriteLine(computer.Fqdn);
                
                var wmiScope = new ManagementScope(String.Format("\\\\{0}\\root\\cimv2", computer.Fqdn), connectoptions);

                //ManagementScope manScope = new ManagementScope(computer.Fqdn, connOptions);
                

                wmiScope.Connect();
                ObjectGetOptions objectGetOptions = new ObjectGetOptions();
                ManagementPath managementPath = new ManagementPath("Win32_ScheduledJob");
                ManagementClass processClass = new ManagementClass(wmiScope, managementPath, objectGetOptions);


                var processToRun = new[] { command };
                */

                /*
                ManagementBaseObject inParams = processClass.GetMethodParameters("Create");
                //inParams["Name"] = "TESTER";
                inParams["Owner"] = "Tester";
                inParams["Command"] = "ipconfig.exe";
                //inParams["StartTime"] = "********171000.000000-300";
                */


                /*


                ManagementBaseObject inParams = processClass.GetMethodParameters("Create");
                inParams["Caption"] = "Suspicious ScheduledTask";
                inParams["Command"] = "iponfig.exe";
                //inParams["TaskName"] = "TESTER";
                string StartTime = DateTime.Now.AddMinutes(1).ToUniversalTime().ToString();
                inParams["StartTime"] = "********171000.000000-300";

                //Console.WriteLine("got this far #1");
                var wmiProcess = new ManagementClass(wmiScope, new ManagementPath("Win32_ScheduledJob"), new ObjectGetOptions());
                //Console.WriteLine("got this far #2");
                ManagementBaseObject outParams = processClass.InvokeMethod("Create", inParams, null);
                //Console.WriteLine("got this far #3");
                //wmiProcess.InvokeMethod("Create", processToRun);


                
                strJobId = outParams["JobId"].ToString();
                Console.WriteLine("Out parameters:");
                Console.WriteLine("JobId: " + outParams["JobId"]);

                Console.WriteLine("ReturnValue: " + outParams["ReturnValue"]);
                

                DateTime dtime = DateTime.Now;
                Console.WriteLine("{0}[{1}] Successfully created a scheduled Task using WMI on {2}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                */

                /*
                string strJobId;
                int DaysOfMonth = 0;
                int DaysOfWeek = 0;
                ManagementClass classInstance = new ManagementClass(String.Format("\\\\{0}\\root\\cimv2", computer.Fqdn), "Win32_ScheduledJob", null);
                ManagementBaseObject inParams = classInstance.GetMethodParameters("Create");
                inParams["Name"] = "TestTestTest";
                inParams["Command"] = "Notepad.exe";
                inParams["InteractWithDesktop"] = false;
                inParams["RunRepeatedly"] = true;
                if (DaysOfMonth > 0)
                    inParams["DaysOfMonth"] = 0;
                if (DaysOfWeek > 0)
                    inParams["DaysOfWeek"] = 0;
                inParams["StartTime"] = "20101129105409.000000+330";
                ManagementBaseObject outParams = classInstance.InvokeMethod("Create", inParams, null);

                strJobId = outParams["JobId"].ToString();
                Console.WriteLine("Out parameters:");
                Console.WriteLine("JobId: " + outParams["JobId"]);

                Console.WriteLine("ReturnValue: " + outParams["ReturnValue"]);
                */

                /*
                ConnectionOptions connection = new ConnectionOptions();
                var wmiScope = new ManagementScope(string.Format(@"\\{0}\root\CIMV2", computer.Fqdn), connection);
                wmiScope.Connect();
                ObjectGetOptions objectGetOptions = new ObjectGetOptions();
                ManagementPath managementPath = new ManagementPath(path: "Win32_ScheduledJob");

                DateTime currentTime = DateTime.Now;

                ManagementClass classInstance = new ManagementClass(scope: wmiScope, path: managementPath, options: objectGetOptions);
                ManagementBaseObject inParams = classInstance.GetMethodParameters("Create");
                //inParams["Name"] = "TestTest";
                inParams["Command"] = "cmd.exe";
                //inParams["StartTime"] = string.Format("********{0}{1}{2}.000000+{3}", currentTime.Hour, currentTime.Minute, currentTime.Second, 240);
                inParams["InteractWithDesktop"] = true;

                ManagementBaseObject outParams = classInstance.InvokeMethod("Create", inParams, null);
                Console.WriteLine("JobId: " + outParams["jobId"]);
                Console.WriteLine("ReturnValue: " + outParams["returnValue"]);
                Console.ReadKey();
                */


            }
            catch (Exception ex)
            {
                DateTime dtime = DateTime.Now;
                if (ex.Message.Contains("ACCESSDENIED")) Console.WriteLine("{0}[{1}] Failed to execute execute a process using WMI on {2}. (Access Denied)", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn);
                else Console.WriteLine("{0}[{1}] Failed to execute a process using WMI on {2}. {3}", "".PadLeft(4), dtime.ToString("MM/dd/yyyy HH:mm:ss"), computer.Fqdn, ex.GetType());
                Console.WriteLine(ex);
            }


        }




    }
}
