#r "Newtonsoft.Json"
using System.Net;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;


public class ExeArg
{
    public string Name { get; set; }
    public string Type { get; set; }
    public string Value { get; set; }
}

//this is to delete the folder after the response
public class FileHttpResponseMessage : HttpResponseMessage
{
    private string filePath;

    public FileHttpResponseMessage(string filePath)
    {
        this.filePath = filePath;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Content.Dispose();

        Directory.Delete(filePath,true);
    }
}

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

   
    string localPath = req.RequestUri.LocalPath;
    string functionName = localPath.Substring(localPath.LastIndexOf('/')+1);

    var json = File.ReadAllText(string.Format(@"D:\home\site\wwwroot\{0}\FunctionConfig.json",functionName));

    var config = JsonConvert.DeserializeObject<dynamic>(json);

    var functionArguments = config.input.arguments;
    var localOutputFolder = Path.Combine(@"d:\home\data", config.output.folder.Value, Path.GetFileNameWithoutExtension(Path.GetTempFileName()));
    var workingDirectory = Path.Combine(@"D:\home\site\wwwroot", config.name.Value);
    Directory.SetCurrentDirectory(workingDirectory);

    var command = config.input.command.Value;

    string outputFile = config.output.binaryFile.returnFile.Value;
    string outputFileName = config.output.binaryFile.returnFileName.Value;

    var argList = new List<ExeArg>();

    //handle file system, local file etc. and construct the input params for the actual calling of the .exe
    foreach (var arg in functionArguments)
    {
        var exeArg = new ExeArg();
        exeArg.Name = arg.Name;
        var value = (Newtonsoft.Json.Linq.JObject)arg.Value;
        var property = (Newtonsoft.Json.Linq.JProperty)value.First;
        exeArg.Type = property.Name;
        exeArg.Value = property.Value.ToString();

        var valueFromQueryString = getValueFromQuery(req, exeArg.Name);

        log.Info("valueFromQueryString name=" + exeArg.Name);
        log.Info("valueFromQueryString val=" + valueFromQueryString);
        if(!string.IsNullOrEmpty(valueFromQueryString))
        {
            exeArg.Value = valueFromQueryString;
            log.Info(exeArg.Name + " " + valueFromQueryString);
        }
        

        if(exeArg.Type.ToLower() == "localfile" || exeArg.Type.ToLower() == "localfolder")
        {
            exeArg.Value = Path.Combine(localOutputFolder, exeArg.Value);
            exeArg.Type = "string";
        }
        if(string.IsNullOrEmpty(exeArg.Value))
        {
            //throw exception here
        }
        argList.Add(exeArg);
    }
    int i = 0;

    //call the exe
    Dictionary<string, string> paramList = ProcessParameters(argList, localOutputFolder);
    foreach (string parameter in paramList.Keys)
    {
        command = command.Replace(parameter, paramList[parameter]);
    }
    string commandName = command.Split(' ')[0];
    string arguments = command.Split(new char[] { ' ' }, 2)[1];
    Process.Start(commandName, arguments).WaitForExit();



    //handle return file
    var path = Directory.GetFiles(localOutputFolder, outputFile)[0];
    //HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK);
    var result = new FileHttpResponseMessage(localOutputFolder);
    var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
    result.Content = new StreamContent(stream);
    result.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
    result.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
    {
        FileName = outputFileName
    };

    return result;
    //do clean up
}

private static Dictionary<string, string> ProcessParameters(List<ExeArg> arguments, string outputFolder)
{
    Dictionary<string, string> paramList = new Dictionary<string, string>();
    foreach (var arg in arguments)
    {
        switch (arg.Type)
        {
            case "url":
                string downloadedFileName = ProcessUrlType((string)arg.Value, outputFolder);
                paramList.Add("{" + arg.Name + "}", downloadedFileName);
                break;
            case "string":
                paramList.Add("{" + arg.Name + "}", arg.Value.ToString());
                break;
            default:
                break;
        }
    }
    return paramList;
}

private static string ProcessUrlType(string url, string outputFolder)
{
    Directory.CreateDirectory(outputFolder);
    string downloadedFile = Path.Combine(outputFolder, Path.GetFileName(Path.GetTempFileName()));
    //for oneDrive links 
    HttpWebRequest webRequest = (HttpWebRequest)HttpWebRequest.Create(url);
    webRequest.AllowAutoRedirect = false;
    WebResponse webResp = webRequest.GetResponse();
    webRequest = (HttpWebRequest)HttpWebRequest.Create(webResp.Headers["Location"].Replace("redir", "download"));
    webResp = webRequest.GetResponse();
    string fileUrl = webResp.Headers["Content-Location"];

    WebClient webClient = new WebClient();
    webClient.DownloadFile(fileUrl, downloadedFile);
    return downloadedFile;
}

private static string getValueFromQuery(HttpRequestMessage req, string name)
{
    // parse query parameter
    string value = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, name, true) == 0)
        .Value;
    return value;
}