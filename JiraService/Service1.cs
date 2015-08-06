using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.IO;


namespace JiraService
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();


            CookieContainer cookies = new CookieContainer();
            FilterResults results = new FilterResults();
            string url;
            DateTime sprintStartDate = DateTime.Now;
            DateTime sprintEndDate = DateTime.Now;

           // DateTime createDate = DateTime.Now;

            //  url = "http://dev-aus-jira-01.swdev.local/rest/api/2/search?jql=project+%3D+%22Unified+IT+Manager%22+AND+created+%3E+startOfMonth%28%29";
            // http://dev-aus-jira-01.swdev.local/rest/api/2/search?jql=project+%3D+%22Unified+IT+Manager%22+AND+sprint+in+openSprints%28%29



            cookies = jiraAuthentication();
            getStartAndEndDate(cookies, out sprintStartDate, out sprintEndDate);
            url = "http://dev-aus-jira-01.swdev.local/rest/api/2/search?jql=project+%3D+%22Unified+IT+Manager%22+AND+created>" + "'" + sprintStartDate.ToString("yyyy-MM-dd") + "'" + "AND+created<=" + "'" + sprintEndDate.ToString("yyyy-MM-dd") + "'";
            results = deserializeFilterResults(url, cookies);

            //foreach (var tic in results.issues)
            //{
            //    DateTime.TryParse(tic.fields.created, out createDate);
            //    Console.WriteLine("{0} priority= {1} status = {2}", tic.key, tic.fields.priority.name, createDate.ToString("yyyy-MM-dd"));
            //}

            //Console.WriteLine(results.issues.Count.ToString());


            getPriorityStats(results.issues);
            getStatusStats(results.issues);
            getBugsPerDayStats(results.issues);
            getUndefinedBugs(results.issues);

            //Console.Write(sprintStartDate.ToString());
            //Console.Write(sprintEndDate.ToString());


            //Console.ReadKey();
        }

        protected override void OnStart(string[] args)
        {
        }

        protected override void OnStop()
        {
        }

        static CookieContainer jiraAuthentication()
        {
            CookieContainer cookies = new CookieContainer();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://dev-aus-jira-01.swdev.local/rest/api/2/dashboard?os_username=peter.drobec&os_password=pe_dro123");
            request.CookieContainer = cookies;
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Console.WriteLine(cookies.Count.ToString());
            return cookies;
        }

        //Method for deserialization of JSON for particular jira issue
        static JiraTicket deserializeTicket(string url, CookieContainer cookies)
        {
            string ticketJson;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.CookieContainer = cookies;
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            ticketJson = readHttpResponse(response);

            JiraTicket ticket = JsonConvert.DeserializeObject<JiraTicket>(ticketJson, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return ticket;
        }

        //Method for deserialization filter results to objects (including List of Jira ticket)
        static FilterResults deserializeFilterResults(string url, CookieContainer cookies)
        {
            string resultJson;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.CookieContainer = cookies;

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            resultJson = readHttpResponse(response);

            FilterResults results = JsonConvert.DeserializeObject<FilterResults>(resultJson, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return results;

        }

        //method returns start and end date of running sprints
        static void getStartAndEndDate(CookieContainer cookies, out DateTime start, out DateTime end)
        {
            string resultJson;
            List<string> sprintId = new List<string>();
            DateTime tempDt1;
            DateTime tempDt2;
            start = System.DateTime.Now;
            end = System.DateTime.Now;

            //getting list of sprints for given rapidboard id = 360
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://dev-aus-jira-01.swdev.local/rest/greenhopper/1.0/sprintquery/360");
            request.CookieContainer = cookies;

            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            resultJson = readHttpResponse(response);

            JObject o = JObject.Parse(resultJson);
            JArray sprints = (JArray)o["sprints"];

            //selecting active sprints Ids
            var activeSprintIds =
                from f in sprints
                where (string)f["state"] == "ACTIVE"
                select (string)f["id"];

            string resultJson2;
            List<string> sprintDetails = new List<string>();

            //getting json with details of all active sprints
            foreach (var id in activeSprintIds)
            {
                HttpWebRequest request2 = (HttpWebRequest)WebRequest.Create("http://dev-aus-jira-01.swdev.local/rest/greenhopper/1.0/sprint/" + id + "/edit/model");
                request2.CookieContainer = cookies;
                HttpWebResponse response2 = (HttpWebResponse)request2.GetResponse();
                resultJson2 = readHttpResponse(response2);
                sprintDetails.Add(resultJson2);
            }


            foreach (var dt in sprintDetails)
            {
                JObject ob = JObject.Parse(dt);
                DateTime.TryParse((string)ob["sprint"]["startDate"], out tempDt1);
                if (tempDt1 < start) start = tempDt1;
                DateTime.TryParse((string)ob["sprint"]["endDate"], out tempDt2);
                if (tempDt2 > end) end = tempDt2;
            }

        }

        //this method reads the http response to string variable
        static string readHttpResponse(HttpWebResponse response)
        {
            string resultJson;
            using (var s = new StreamReader(response.GetResponseStream()))
            {
                resultJson = s.ReadToEnd();
            }
            return resultJson;
        }

        //this method calculates count of opened issues grouped by priority
        static void getPriorityStats(List<JiraTicket> jtickets)
        {
            var priorityGroups =
                from ticket in jtickets
                group ticket by ticket.fields.priority.name into priorityGroup
                select priorityGroup;

            using (var w = new StreamWriter(@"C:\priorityStats.csv"))
            {
                w.WriteLine("Priority,Count");
                foreach (var pg in priorityGroups)
                {
                    var line = string.Format("{0},{1}", pg.Key, pg.Count<JiraTicket>().ToString());
                    w.WriteLine(line);
                    w.Flush();
                }
            }
        }

        //this method calculates count of issues in individual status
        static void getStatusStats(List<JiraTicket> jtickets)
        {
            var statGroups =
                from ticket in jtickets
                group ticket by ticket.fields.status.name into statGroup
                select statGroup;

            using (var w = new StreamWriter(@"C:\statusStats.csv"))
            {
                w.WriteLine("Status,Count");
                foreach (var sg in statGroups)
                {
                    var line = string.Format("{0},{1}", sg.Key, sg.Count<JiraTicket>().ToString());
                    w.WriteLine(line);
                    w.Flush();                    
                }
            }
        }

        //this method returns count of issues opened in individual days
        static void getBugsPerDayStats(List<JiraTicket> jtickets)
        {
            var dayGroups =
                from ticket in jtickets
                group ticket by DateTime.Parse(ticket.fields.created).ToString("yyyy/MM/dd") into dayGroup
                select dayGroup;

           using (var w = new StreamWriter(@"C:\bugsPerDayStats.csv"))
            {
                w.WriteLine("Day,Count");
                foreach (var dg in dayGroups)
                {
                    var line = string.Format("{0},{1}", dg.Key, dg.Count<JiraTicket>().ToString());
                    w.WriteLine(line);
                    w.Flush();
                }
            }            
        }

        //this method returns list of items with undefined priority and creator name
        static void getUndefinedBugs(List<JiraTicket> jtickets)
        {
            var undefined =
               from ticket in jtickets
               where ticket.fields.priority.name == "Undefined"
               select new { ticket.key, ticket.fields.creator.displayName };

            using (var w = new StreamWriter(@"C:\undefinedBugs.csv"))
            {
                w.WriteLine("Issue,Creator");
                foreach (var un in undefined)
                {
                    var line = string.Format("{0},{1}", un.key, un.displayName);
                    w.WriteLine(line);
                    w.Flush();
                }
            }
        }
    }
}
