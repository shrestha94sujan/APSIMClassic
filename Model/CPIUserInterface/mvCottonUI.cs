﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Xml;
using System.Windows.Forms;

using ApsimFile;
using CMPServices;
using Controllers;
using CSGeneral;

namespace CPIUserInterface
{
    //=====================================================================
    /// <summary>
    /// Specialised interface for mvCotton component. 
    /// This mvCottonUI class is used to create a initsection for Cotton
    /// from the component's getDescription.
    /// Cultivar details listed in the model.xml (apsim) are read and used to 
    /// populate the cultivar array in the initSection.
    /// Other details are set to default values from the getDescription().
    /// Cultivar, sow depth, row spacing, plants density and skip row are 
    /// specified/adjusted in the sow command (manager).
    /// </summary>
    //=====================================================================
    public partial class mvCottonUI : CPIBaseView
    {

        private List<TTypedValue> typedvals;

        //=====================================================================
        /// <summary>
        /// A tree grid UI that allows the initialisation of a list of 
        /// SDML values as found in the initsection of a CPI component.
        /// </summary>
        //=====================================================================
        public mvCottonUI() : base()
        {
            InitializeComponent();
            typedvals = new List<TTypedValue>();

            this.afTreeViewColumns1.reloadTreeEvent += new AFTreeViewColumns.reloadTree(afTreeViewColumns1_reloadTreeEvent);
            this.afTreeViewColumns1.saveChangesEvent += new AFTreeViewColumns.onDataChange(afTreeViewColumns1_saveChangesEvent);

            ListView.ColumnHeaderCollection lvColumns = afTreeViewColumns1.Columns;

            lvColumns.Clear();

            ColumnHeader ch1 = new ColumnHeader();
            ch1.Name = "Variable";
            ch1.Text = "Variable";
            ch1.Width = 150;
            lvColumns.Add(ch1);

            ColumnHeader ch2 = new ColumnHeader();
            ch2.Name = "Value";
            ch2.Text = "Value";
            ch2.Width = 150;
            lvColumns.Add(ch2);

            ColumnHeader ch3 = new ColumnHeader();
            ch3.Name = "Type";
            ch3.Text = "Type";
            ch3.Width = 50;
            lvColumns.Add(ch3);

            ColumnHeader ch4 = new ColumnHeader();
            ch4.Name = "Unit";
            ch4.Text = "Unit";
            ch4.Width = 50;
            lvColumns.Add(ch4);

            ColumnHeader ch5 = new ColumnHeader();
            ch5.Name = "default";
            ch5.Text = "default";
            ch5.Width = 50;
            lvColumns.Add(ch5);

            ColumnHeader ch6 = new ColumnHeader();
            ch6.Name = "min";
            ch6.Text = "min";
            ch6.Width = 50;
            lvColumns.Add(ch6);

            ColumnHeader ch7 = new ColumnHeader();
            ch7.Name = "max";
            ch7.Text = "max";
            ch7.Width = 50;
            lvColumns.Add(ch7);
        }
        //=====================================================================
        /// <summary>
        /// When the UI is created and loaded
        /// </summary>
        //=====================================================================
        protected override void OnLoad()
        {
            panel1.Visible = true;
            panel2.Visible = false;

            base.OnLoad();

            base.HelpText = " Cotton component";
            
            //find the full path to the dll for the component (normally use InitFromComponentDescription() )
            String ComponentType = Controller.ApsimData.Find(NodePath).Type;
            List<String> DllFileNames = Types.Instance.Dlls(ComponentType);
            FDllFileName = DllFileNames[0];
            FDllFileName = Configuration.RemoveMacros(FDllFileName);

            InitFromComponentDescription(); //fills the propertyList with init properties
        }
        //=====================================================================
        /// <summary>
        /// 
        /// </summary>
        //=====================================================================
        private void mvCottonUI_Load(object sender, EventArgs e)
        {
        }
        //=====================================================================
        /// <summary>
        /// Load an image and set the labels
        /// </summary>
        //=====================================================================
        public override void OnRefresh()
        {
            label1.Text = XmlHelper.Type(Data);
            SetHelpMsg();

            String imagefile = Types.Instance.MetaData(Data.Name, "image");
            if (File.Exists(imagefile))
            {
                pictureBox1.Image = Image.FromFile(imagefile);
            }
            else
            {
                pictureBox1.Image = null;
            }
            label2.Text = Types.Instance.MetaData(Data.Name, "description");
           
            //Fill the property fields
            XmlNode initSection = XmlHelper.Find(Data, "initsection");

            if (initSection != null)
            {
                InitFromInitSection(initSection.OuterXml);
            }

        }
        //=======================================================================
        /// <summary>
        /// CPI components also include some properties that may not need to be shown to the user.
        /// </summary>
        /// <param name="propName">Name of the property</param>
        /// <returns>True if the property should be shown.</returns>
        //=======================================================================
        private Boolean makePropertyVisible(String propName)
        {
            Boolean bPropVisible = true;
            //check if this variable should be hidden
            if ((propName == STRSUBEVENT_ARRAY))
                bPropVisible = false;
            if ((propName == STRPUBEVENT_ARRAY))
                bPropVisible = false;
            if ((propName == STRDRIVER_ARRAY))
                bPropVisible = false;

            return bPropVisible;
        }
        //=======================================================================
        /// <summary>
        /// Initialise the lists of properties with values from the init section
        /// SDML. And reload the tree with the values.
        /// </summary>
        /// <param name="initXML">The init section which is <code><initsection>...</initsection></code></param>
        //=======================================================================
        private Boolean InitFromInitSection(String initXML)
        {
            if (initXML.Length > 0)
            {
                typedvals.Clear();
                TInitParser initPsr = new TInitParser(initXML);

                for (int i = 1; i <= initPsr.initCount(); i++)
                {
                    String initText = initPsr.initText((uint)i);
                    TSDMLValue sdmlinit = new TSDMLValue(initText, "");
                    typedvals.Add(sdmlinit);
                }
            }
            //if the component description is valid then use it.
            if (propertyList.Count > 0)
            {
                foreach (TTypedValue value in typedvals)    //for every init section value
                {
                    //find the property in the component description list
                    int i = 0;
                    TCompProperty prop = propertyList[i];
                    while ((prop != null) && (i < propertyList.Count))
                    {
                        if (value.Name.ToLower() == prop.Name.ToLower())
                        {
                            prop.InitValue.setValue(value); //set the property's value
                        }
                        i++;
                        if (i < propertyList.Count)
                            prop = propertyList[i];
                    }
                }
            }
            if (propertyList.Count > 0)
                populateTreeModel(propertyList);            //can populate with the full list
            else
                populateTreeModel();                        //use init section only

            return (initXML.Length > 0) || (propertyList.Count > 0);
        }
        //=======================================================================
        /// <summary>
        /// Populate the tree based on the list of typed values found only in
        /// the init section for the component.
        /// </summary>
        private void populateTreeModel()
        {
            afTreeViewColumns1.SuspendLayout();
            afTreeViewColumns1.TreeView.BeginUpdate();
            afTreeViewColumns1.TreeView.Nodes.Clear();

            foreach (TTypedValue prop in typedvals)
            {
                if (makePropertyVisible(prop.Name) == true)
                {
                    TreeNode trNode2 = new TreeNode();
                    afTreeViewColumns1.TreeView.Nodes.Add(trNode2);
                    addTreeModelNode(trNode2, prop.Name, prop);
                }
            }
            afTreeViewColumns1.TreeView.EndUpdate();
            afTreeViewColumns1.ResumeLayout();
        }
        //=======================================================================
        /// <summary>
        /// Populate the tree based on the TCompProperty list gained from the
        /// component description.
        /// </summary>
        private void populateTreeModel(List<TCompProperty> compProperties)
        {
            afTreeViewColumns1.SuspendLayout();
            afTreeViewColumns1.TreeView.BeginUpdate();
            afTreeViewColumns1.TreeView.Nodes.Clear();

            TCompProperty prop;
            for (int i = 0; i <= compProperties.Count - 1; i++)
            {
                prop = compProperties[i];
                if (prop.bInit == true)
                {
                    if (makePropertyVisible(prop.Name) == true)
                    {
                        TreeNode trNode2 = new TreeNode();
                        afTreeViewColumns1.TreeView.Nodes.Add(trNode2);
                        addTreeModelNode(trNode2, prop.InitValue.Name, prop.InitValue);
                    }
                }
            }
            afTreeViewColumns1.TreeView.EndUpdate();
            afTreeViewColumns1.ResumeLayout();
        }
        //=======================================================================
        private void addTreeModelNode(TreeNode parentNode, String name, TTypedValue typedValue)
        {
            uint i = 1;
            uint j = 1;

            parentNode.Name = name;
            parentNode.Text = name;
            parentNode.Tag = new TAFTreeViewColumnTag(typedValue);

            if ((typedValue.isArray()) || (typedValue.isRecord()))
            {
                uint iCount = typedValue.count();
                while (i <= iCount)
                {
                    TTypedValue typedValueChild = typedValue.item(i);

                    if (typedValueChild != null)
                    {
                        TreeNode trNode2 = new TreeNode();
                        parentNode.Nodes.Add(trNode2);
                        string sVarName = j.ToString();
                        if (typedValue.isArray())
                            sVarName = "[" + sVarName + "]";
                        j++;
                        if (typedValueChild.Name.Length > 0)
                        {
                            sVarName = typedValueChild.Name;
                        }
                        addTreeModelNode(trNode2, sVarName, typedValueChild);
                    }
                    i++;
                }
            }
        }
        //=====================================================================
        /// <summary>
        /// Save the changes from the form.
        /// </summary>
        //=====================================================================
        public override void OnSave()
        {
           //************** //StoreControls();
            String newXML = WriteInitsectionXml();

            //now store the new xml by replacing the old xmlnode in Data
            XmlNode initSection = XmlHelper.Find(Data, "initsection");

            if (initSection != null)
                Data.RemoveChild(initSection);

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(newXML);

			////skip these few lines as this was duplicating the cultivars node
			//// and causing newly added cultivars to be ignored/lost on the reload
				////add new data about the cultivars as an array of cultivars (only add the ones that have extra details)
				//String CultivarSpecs = WriteCultivars();
				//XmlDocument cultivar = new XmlDocument();
				//cultivar.LoadXml(CultivarSpecs);
				//doc.DocumentElement.AppendChild(doc.ImportNode(cultivar.DocumentElement, true));

            Data.AppendChild(Data.OwnerDocument.ImportNode(doc.DocumentElement, true));
        }
        //=====================================================================
        /// <summary>
        /// Write the TTypedValues to an xml string.
        /// </summary>
        /// <returns>The init section string</returns>
        //=====================================================================
        protected override String WriteInitsectionXml()
        {
            StringBuilder newXML = new StringBuilder();
            newXML.Append("<initsection>");

            TSDMLValue sdmlWriter = new TSDMLValue("<init/>", "");

            if (propertyList.Count > 0)             //if using the full component description
            {
                for (int i = 0; i < propertyList.Count; i++)
                {
                    if (propertyList[i].bInit == true)
                        newXML.Append(sdmlWriter.getText(propertyList[i].InitValue, 0, 2));
                }
            }
            else
            {
                for (int i = 0; i < typedvals.Count; i++)
                {
                    newXML.Append(sdmlWriter.getText(typedvals[i], 0, 2));
                }
            }

            newXML.Append("</initsection>");
            return newXML.ToString();
        }
        //=====================================================================
        /// <summary>
        /// The ddml type string for the cultivar type that will be in the init section of the component
        /// </summary>
        private const String CULTIVARTYPE = "<type name=\"cultivars\" kind=\"defined\" array=\"T\">" +
                                              "<element>" +
                                                "<field name=\"cultivar\" kind=\"string\"/>" +
                                                "<field name=\"percent_l\" kind=\"double\"/>" +
                                                "<field name=\"scboll\" kind=\"double\"/>" +
                                                "<field name=\"respcon\" kind=\"double\"/>" +
                                                "<field name=\"sqcon\" kind=\"double\"/>" +
                                                "<field name=\"fcutout\" kind=\"double\"/>" +
                                                "<field name=\"flai\" kind=\"double\"/>" +
                                                "<field name=\"DDISQ\" kind=\"double\"/>" +
                                                "<field name=\"TIPOUT\" kind=\"double\"/>" +
                                                "<field name=\"FRUDD\" kind=\"double\" array=\"T\"/>" +
                                                "<field name=\"BLTME\" kind=\"double\" array=\"T\"/>" +
                                                "<field name=\"WT\" kind=\"double\" array=\"T\"/>" +
                                                "<field name=\"dlds_max\" kind=\"double\"/>" +
                                                "<field name=\"rate_emergence\" kind=\"double\" units=\"mm/dd\"/>" +
                                                "<field name=\"popcon\" kind=\"double\"/>" +
                                                "<field name=\"fburr\" kind=\"double\"/>" +
                                                "<field name=\"ACOTYL\" kind=\"double\" units=\"mm2\"/>" +
                                                "<field name=\"RLAI\" kind=\"double\"/>" +
                                                "<field name=\"BckGndRetn\" kind=\"double\"/>" +
                                              "</element>" +
                                            "</type>";

        //=====================================================================
        /// <summary>
        /// Scan through the cultivar list in mvCotton.xml and find any that have
        /// parameters and form them into an XML document based on TSDMLValue.
        /// </summary>
        /// <returns>The cultivar list as XML.</returns>
        //=====================================================================
        private String WriteCultivars()
        {
            //create new TSDMLValue for the cultivar details
            TDDMLValue CultivarValues = new TDDMLValue(CULTIVARTYPE, "");
            CultivarValues.setElementCount(0);

            uint cultivarCount = 0;
            XmlDocument ModelDoc = new XmlDocument();
            String ComponentType = Controller.ApsimData.Find(NodePath).Type;
            ModelDoc.LoadXml("<Model>" + Types.Instance.ModelContents(ComponentType) + "</Model>");
            foreach (XmlNode Child in ModelDoc.DocumentElement.ChildNodes)
            {
                if (XmlHelper.Attribute(Child, "cultivar").ToLower() == "yes")
                {
                    if (Child.ChildNodes.Count > 0)
                    {
                        XmlDocument CultivarDoc = new XmlDocument();
                        CultivarDoc.LoadXml(Child.OuterXml);
                        CultivarValues.setElementCount(++cultivarCount);
                        TTypedValue arrayitem = CultivarValues.item(cultivarCount);
                        arrayitem.member("cultivar").setValue(CultivarDoc.DocumentElement.Name);   //read root tag name
                        foreach (XmlNode param in CultivarDoc.DocumentElement.ChildNodes)  //for each child of cultivardoc
                        {
                            if (param.NodeType == XmlNodeType.Element)
                            {
                                TTypedValue field = arrayitem.member(param.Name);
                                if (field != null)
                                {
                                    if (param.Name == "FRUDD" || param.Name == "BLTME" | param.Name == "WT")
                                    {
                                        //add array node values
                                        String arrayField = param.InnerText;
                                        if (arrayField.Contains(" "))
                                        {
                                            string[] VariableNameBits = arrayField.Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                                            double[] values = MathUtility.StringsToDoubles(VariableNameBits);
                                            uint count = 0;
                                            foreach (double val in values)
                                            {
                                                field.setElementCount(++count);
                                                field.item(count).setValue(val);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        double val = Convert.ToDouble(param.InnerText);     //these are all doubles
                                        field.setValue(val);        //copy child to new doc
                                    }
                                }
                                else
                                    throw new Exception("Cannot set init value for " + param.Name + " in WriteCultivars()");
                            }
                        }
                    }
                }
            }
            TSDMLValue writer = new TSDMLValue("<type/>", "");
            return writer.getText(CultivarValues, 0, 2);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            panel2.Visible = true;
            panel1.Visible = false;
            SetHelpMsg();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            panel1.Visible = true;
            panel2.Visible = false;
            SetHelpMsg();
        }
        private void SetHelpMsg()
        {
            if (panel1.Visible == true)
                this.HelpText = "This module does not require editing of it's properties.";
            else
                this.HelpText = "Edit these values with care.";
        }
        //=======================================================================
        private void afTreeViewColumns1_saveChangesEvent()
        {
            this.afTreeViewColumns1.Focus();
        }
        //=======================================================================
        /// <summary>
        /// Called from the tree when arrays are resized.
        /// Using the selected node, just recreate it's child nodes.
        /// </summary>
        //=======================================================================
        private void afTreeViewColumns1_reloadTreeEvent()
        {
            if (afTreeViewColumns1.TreeView.SelectedNode != null)
            {
                TAFTreeViewColumnTag changedValue = (TAFTreeViewColumnTag)afTreeViewColumns1.TreeView.SelectedNode.Tag;
                afTreeViewColumns1.TreeView.BeginUpdate();
                afTreeViewColumns1.TreeView.SelectedNode.Nodes.Clear();
                addTreeModelNode(afTreeViewColumns1.TreeView.SelectedNode, changedValue.TypedValue.Name, changedValue.TypedValue);
                afTreeViewColumns1.TreeView.SelectedNode.Expand();
                afTreeViewColumns1.TreeView.EndUpdate();
            }
        }
    }
}
