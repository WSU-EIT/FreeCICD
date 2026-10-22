namespace FreeCICD.Client;

public partial class BlazorDataModel
{
    private bool HaveDeletedRecordsApp {
        get {
            bool output = false;

            // Check your app-specific deleted records here.
            //if (DeletedRecordCounts.MyValue > 0 ) {
            //    output = true;
            //}

            return output;
        }
    }
}