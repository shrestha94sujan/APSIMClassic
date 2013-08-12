#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <iostream>

#ifdef __WIN32__
#include <windows.h>
#include <ShlObj.h>
#else
#include <dlfcn.h>
#endif

#include <General/platform.h>
#include <General/string_functions.h>
#include <General/stristr.h>
#include <General/path.h>
#include <General/dll.h>
#include <General/io_functions.h>

#include <ComponentInterface2/ScienceAPI2.h>

#include "RComponent.h"

using namespace std;

RComponent *currentRComponent = NULL;
static bool hasStartedR = false;

extern "C" EXPORT RComponent  * STDCALL createComponent(ScienceAPI2& scienceAPI)
   {
   return new RComponent(scienceAPI);
   }

extern "C" void EXPORT STDCALL deleteComponent(RComponent* component)
   {
   if (component)
     delete component;
   }


//--------------------------- R Embedder DLL initialisation
void *RDLLHandle = NULL;
void *MyDLLHandle = NULL;
typedef bool (*B_VOID_FN)(void);
typedef bool (*B_CHAR_FN)(const char*);
typedef bool (*B_CHAR2_FN)(void *, const char*, const char*);
typedef bool (*B_VOIDPTR_FN)(void *);
typedef bool (*B_VEC_FN)(const char*, char *, unsigned int, unsigned int, unsigned int *);
typedef bool (*B_2CHAR_FN)(const char*, char*, int);

B_CHAR2_FN   R_StartFn;
B_CHAR_FN    R_EvalCharFn;
B_VEC_FN     R_GetVecFn;
B_2CHAR_FN   R_EvalCharSimpleFn;

#ifdef __WIN32__
HMODULE GetCurrentModule()
{ // NB: XP+ solution!
  HMODULE hModule = NULL;
  GetModuleHandleEx(
    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
    (LPCTSTR)GetCurrentModule,
    &hModule);

  return hModule;
}
bool GetStringRegKey(const std::string &strKeyName,const std::string &strValueName, std::string &strValue)
{
   strValue = "";
   HKEY  keys[] = {HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER};
   for (int i = 0; i < 2; i++)
     {
     HKEY CurrentKey = keys[i];
     HKEY hKey;
     LONG lRes = RegOpenKeyEx(CurrentKey, strKeyName.c_str(), 0, KEY_READ, &hKey);
     if (lRes == ERROR_SUCCESS)
        {
        char szBuffer[1024];
        DWORD dwBufferSize = sizeof(szBuffer);
        ULONG nError;
        nError = RegQueryValueEx(hKey, strValueName.c_str(), 0, NULL, (LPBYTE)szBuffer, &dwBufferSize);
        if (ERROR_SUCCESS == nError)
	      {
          strValue = szBuffer;
	      return true;
	      }
        }
     }
   return false;
}
#endif

// Quietly evaluate something
void REvalQ(const char *s)
{
   if (R_EvalCharFn != NULL) R_EvalCharFn(s);
}

void RGetVector(const char *s, std::vector<std::string> &result)
{
   const int numWidth = 16;
   result.clear();

   char *buffer = new char [2048 * numWidth];
   memset(buffer, 0, sizeof(buffer));
   unsigned int numReturned = 0;
   if (R_GetVecFn != NULL) R_GetVecFn (s, buffer, 2048 * numWidth, numWidth, &numReturned);
   result.resize(numReturned);
   char *ptr = buffer;
   for (unsigned int i = 0; i < numReturned; i++)
     {
	 result[i] = ptr;
	 ptr += numWidth;
	 }
   delete [] buffer;
}

// Evaluate something and return as concantenated string
std::string SimpleREval(const char *s)
{
   char buffer[1024];
   if (R_EvalCharSimpleFn != NULL) R_EvalCharSimpleFn(s, buffer, sizeof(buffer));
   return (std::string(buffer));
}

bool isNumeric(const std::string &s)
{
  char * p;
  std::strtod( s.c_str(), & p );
  if ( * p == 0 )  return true;
  return false;
}

// ------------------------------------------------------------------
// Initialise and destroy the component.
// ------------------------------------------------------------------
RComponent::RComponent(ScienceAPI2 & api) : apsimAPI(api)
   {
   hasFatalError = false;
   apsimAPI.subscribe("error", nullFunction(&RComponent::onError));
   apsimAPI.subscribe("init2", nullFunction(&RComponent::onInit2));
   }

RComponent::~RComponent(void)
   {
   if (RDLLHandle != NULL) 
   	   {
       B_VOID_FN R_StopFn;
       R_StopFn = (B_VOID_FN) dllProcAddress(RDLLHandle, "EmbeddedR_Stop");
       if (R_StopFn != NULL) R_StopFn();
       }
   RDLLHandle = NULL;
   }


void RComponent::oneTimeInit(void)
{
    apsimAPI.write("RLink Initialisation\n");

    string installPath = "";
    string versionString = "";
    char *r_home = getenv("R_HOME");
    if (r_home != NULL) {
    	 installPath = r_home;
    } else {
#ifdef __WIN32__
       if (!GetStringRegKey("Software\\R-core\\R", "Current Version", versionString))
           if (!GetStringRegKey("Software\\R-core\\R32", "Current Version", versionString))
               throw std::runtime_error("No R version info");
   
       string installPathKey = "Software\\R-core\\R\\" + versionString ;
       if (!GetStringRegKey(installPathKey, "InstallPath", installPath))
       {
           installPathKey = "Software\\R-core\\R32\\" + versionString ;
           if (!GetStringRegKey(installPathKey, "InstallPath", installPath))
               throw std::runtime_error("No R install info in " + installPathKey);
       }
       replace(installPath.begin(), installPath.end(), '\\', '/');
#else
       installPath = "/usr/lib/R";
#endif
    }
// Load R.dll first so that the embedder dll resolves to the loaded version and not something unknown
#ifdef __WIN32__
    string dll = installPath + "/bin/i386/R.dll";
#else
    string dll = installPath + "/lib/libR.so";
#endif

    apsimAPI.write("Loading R from " + installPath + "\n");
    loadDLL(dll);

    string userlibs = "";
#ifdef __WIN32__
    // We need to find the user's "My Docments" directory
    // Microsoft hasn't made this simple, and the rules change with different versions of Windows
    // The function used here has been superseded, but still works and is simpler than the
    // more recent alternatives.
    // For an alternative approach, see https://forums.embarcadero.com/thread.jspa?threadID=69238
    char docPath[MAX_PATH + 1];
    if (SHGetSpecialFolderPath(0, docPath, CSIDL_PERSONAL, false))
    {
        // Now see if the user has an R win-library directory
        string testDir = string(docPath) + "/R/win-library/";
        replace(testDir.begin(), testDir.end(), '\\', '/');
        vector<string> vnums;
        split(versionString, ".", vnums);
        if (vnums.size() >= 2) 
            {
                testDir += vnums[0];
                testDir += ".";
                testDir += vnums[1];
                if (DirectoryExists(testDir)) 
                   userlibs = testDir;
            }
    }
#else
    char *home = getenv("HOME");
    if (home != NULL) {userlibs = string(home) + "/R/" ; } //??? FIXME
#endif    
    if (userlibs != "") 
        apsimAPI.write("Userlibs = " + userlibs + "\n");

    // Preload each dll. Hopefully any inter-dependancies will sort themselves out. 
    string libs[] = {"Rcpp", "RInside"};
    vector<string> paths;

    paths.push_back(installPath + "/library");                       // /usr/lib/R/library
#ifndef __WIN32__
    paths.push_back("/usr/local/lib/R/site-library");
#endif
    // FIXME userlibs ...
    for (unsigned int lib = 0; lib < 2; lib++) 
    {
    RDLLHandle = NULL;
    for (unsigned  int path = 0; path < paths.size(); path++) 
       {
       dll = paths[path] + "/" + libs[lib] ;
#ifndef __WIN32__
       dll +=  "/lib/lib"+ libs[lib] + ".so";
#else
       dll +=  "/libs/i386/" + libs[lib] + ".dll";
#endif
       //apsimAPI.write("Trying to load " +  dll + "\n");
       if (fileExists(dll) && (RDLLHandle = loadDLL(dll)) != NULL)
       	  break;
       } /* for path ...*/
    if (RDLLHandle == NULL)
       apsimAPI.write("Warning: couldnt load R library " +  libs[lib] + "\n");
    }

    dll = fileDirName(apsimAPI.getExecutableFileName()) + "/REmbed";
    replaceAll(dll, "\\", "/");
#ifdef __WIN32__
    dll += ".dll";
#else
    dll += ".so";
#endif

    RDLLHandle = loadDLL(dll);

    R_StartFn = (B_CHAR2_FN) dllProcAddress(RDLLHandle, "EmbeddedR_Start");
    if (R_StartFn == NULL) throw runtime_error("R Embedding DLL missing symbol EmbeddedR_Start");

#ifdef __WIN32__
    MyDLLHandle = (void*)GetCurrentModule();
#else
    MyDLLHandle = RTLD_DEFAULT;
#endif
    if (!R_StartFn(MyDLLHandle, installPath.c_str(), userlibs.c_str()))
       throw runtime_error("R Embedding DLL R_Start() failed");

    if (NULL == (R_EvalCharFn = (B_CHAR_FN) dllProcAddress(RDLLHandle, "EmbeddedR_Eval"))) throw runtime_error("R Embedding DLL missing symbol EmbeddedR_Eval");
    if (NULL == (R_GetVecFn = (B_VEC_FN) dllProcAddress(RDLLHandle, "EmbeddedR_GetVector"))) throw runtime_error("R Embedding DLL missing symbol EmbeddedR_GetVector");
    if (NULL == (R_EvalCharSimpleFn = (B_2CHAR_FN) dllProcAddress(RDLLHandle, "EmbeddedR_SimpleCharEval"))) throw runtime_error("R Embedding DLL missing symbol EmbeddedR_SimpleCharEval");

    // write copyright notice(s).
    apsimAPI.write(SimpleREval("R.version.string") + "\n");
    apsimAPI.write("Copyright (C) 2011 The R Foundation for Statistical Computing\n");
    hasStartedR = true;
}

void RComponent::onInit2(void)
   {
   if (!hasStartedR) this->oneTimeInit();

   rules.clear();
   apsimAPI.readScripts(rules);
   for (map<string,string>::iterator i = rules.begin(); i != rules.end(); i++)
       {
	   if  (i->first != "my variables" && i->first != "apsim variables")
	      {
          string msg;
          msg = "--->Section: "; msg += i->first; msg += "\n";
          msg += i->second; msg += "\n";
          apsimAPI.write(msg);
          if (i->first != "init")
             apsimAPI.subscribe(i->first, namedNullFunction(i->first, &RComponent::onRuleCallback));
          }
	   }

   apsimAPI.write("--->End\n");

   // Expose variables
   vector<string> rawVariables; vector<string> variables; vector<string> units;
   Split_string(rules["my variables"], "\r\n", rawVariables);
   for (vector<string>::iterator i = rawVariables.begin(); i != rawVariables.end(); i++)
       {
	   unsigned int p = i->find('#');
	   if (p != string::npos)
	      *i = i->substr(0, p);
	   vector<string> words;
       SplitStringHonouringQuotes(*i, " ", words);
	   if (words.size() == 1)
	      {variables.push_back(words[0]); units.push_back("");}
	   else if (words.size() == 3 && words[1] == "units")
	      {variables.push_back(words[0]); units.push_back(words[2]);}
	   else if (words.size() > 0)
	   	   throw std::runtime_error("Cant make sense of '" + *i + "'");
       }
   if (variables.size() > 0)
      {
	  apsimAPI.write("--->Exported R Variables:\n");
      for (unsigned int i = 0; i < variables.size(); i++)
	      {
          apsimAPI.write(variables[i]);
          if (units[i] != "") apsimAPI.write(" (" + units[i] + ")");
          apsimAPI.write("\n");
          expose(variables[i], units[i]);
		  }
      }
   // find the apsim variables we want before every event
   variables.clear();
   Split_string(rules["apsim variables"], "\r\n", variables);
   if (variables.size() > 0)
      {
      apsimAPI.write("--->Imported APSIM Variables:\n");
      for (vector<string>::iterator i = variables.begin(); i != variables.end(); i++)
        {
	    unsigned int p = i->find('#');
	    if (p != string::npos)
	      *i = i->substr(0, p);

	    vector<string> words;
        SplitStringHonouringQuotes(*i, " ", words);
	    if (words.size() == 1)
	       {
		   apsimVariables[words[0]] = words[0];
		   apsimAPI.write(words[0] + "\n");
		   }
	    else if (words.size() == 3 && words[1] == "as")
	       {
 	   	   stripLeadingTrailing(words[0], "\"");
	   	   apsimVariables[words[0]] = words[2];
	   	   apsimAPI.write(words[2] + " <- " + words[0] + "\n");
	   	   }
	    else if (words.size() > 0)
	   	   throw std::runtime_error("Cant make sense of '" + *i + "'");
        }
      }

   // Do the init rule if specified..
   string initRule = rules["init"];
   if (initRule != "")
      {
      RComponent *old = currentRComponent; currentRComponent = this;
      importVariables();
      REvalQ(initRule.c_str());
      currentRComponent = old;
      }
   }

void RComponent::onError(void)
   {
   hasFatalError = true;
   }

// Allow apsim to read (and/ or write) one of our variables.
void RComponent::expose(const std::string &variableName, const std::string &units)
   {
   apsimAPI.exposeFunction2(variableName, units, "",
                           StringArrayFunction2(variableName, &RComponent::respondToGet),
                           StringArrayFunction2(variableName, &RComponent::respondToSet));
   }

// Deal with an event coming from the system to us
void RComponent::onRuleCallback(const std::string &s)
   {
   RComponent *old = currentRComponent; currentRComponent = this;
   if (!hasFatalError)
     {
     string rule = rules[s];
     if (rule != "")
       {
       importVariables();
       REvalQ(rule.c_str());
	   }
     }
   currentRComponent = old;
   }

// ------------------------------------------------------------------
// Return a variable to caller. Everything is a string.
// ------------------------------------------------------------------
void RComponent::respondToGet(const std::string &variableName, std::vector<std::string> &result)
   {
   RComponent *old = currentRComponent; currentRComponent = this;
   result.clear();
   RGetVector(variableName.c_str(), result);
   currentRComponent = old;
   }

// ------------------------------------------------------------------
// Set the value of a variable for the specified variable name.
// ------------------------------------------------------------------
void RComponent::respondToSet(const std::string &variableName, std::vector<std::string> &value)
   {
   RComponent *old = currentRComponent; currentRComponent = this;
   string cmd = variableName;
   cmd += "<-";
   if (value.size() > 1)
      {
      cmd += "c(";
      for (std::vector<string>::iterator p = value.begin(); p != value.end(); p++)
	      {
		  if (p != value.begin()) cmd += ",";
		  bool isNum = isNumeric(*p);
		  if (!isNum) cmd += "'";
		  cmd += *p;
		  if (!isNum) cmd += "'";
		  }
      cmd += ")";
      }
   else
      {
      if (isNumeric(value[0]))
	     cmd += value[0];
	  else
         cmd += "'" + value[0] + "'";
	  }
   REvalQ(cmd.c_str());
   currentRComponent = old;
   }

void RComponent::importVariables(void)
{
  for (Name2RuleMap::iterator p = apsimVariables.begin(); p != apsimVariables.end(); p++)
     {
	 vector<string> resultArray;
     apsimAPI.get(p->first, "", 1, resultArray);
	 if (resultArray.size() > 0)
	    respondToSet(p->second, resultArray);
	 }
}

void RComponent::fatal(const std::string &message) 
    {
    throw std::runtime_error(message);
    }
    
