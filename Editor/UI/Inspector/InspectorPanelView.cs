using AppKit;
using CoreGraphics;
using System.Globalization;
using TACTIX.Editor.Scene;
using TACTIX.Engine.Runtime.ECS;

namespace TACTIX.Editor.UI.Inspector;

public sealed class InspectorPanelView : NSView
{
    private readonly World _world;
    private readonly EditorSelection _selection;
    private readonly EditorCommandStack _commands;
    private readonly NSTextField _title;
    private readonly NSTextField[] _fields = new NSTextField[9];
    private readonly NSTextField _componentTitle;
    private readonly NSTextField[] _lightLabels = new NSTextField[4];
    private readonly NSTextField[] _lightFields = new NSTextField[4];

    public InspectorPanelView(CGRect frame, World world, EditorSelection selection, EditorCommandStack commands) : base(frame)
    {
        _world=world; _selection=selection; _commands=commands;
        WantsLayer=true; Layer!.BackgroundColor=NSColor.FromRgb(25,25,28).CGColor;
        _title=Label("Nothing selected",12,frame.Height-38,(nfloat)Math.Max(80.0,(double)frame.Width-24.0),22); AddSubview(_title);

        string[] names={"PX","PY","PZ","RX","RY","RZ","SX","SY","SZ"};
        for(int i=0;i<9;i++)
        {
            int row=i/3,col=i%3;
            var label=Label(names[i],12+col*72,frame.Height-76-row*38,24,22); AddSubview(label);
            var field=new NSTextField(new CGRect(34+col*72,frame.Height-78-row*38,48,24)){StringValue="0"};
            int index=i; field.EditingEnded+=(_,_)=>ApplyTransform(index); _fields[i]=field; AddSubview(field);
        }

        var componentY=frame.Height-205;
        _componentTitle=Label("",12,componentY,(nfloat)Math.Max(80.0,(double)frame.Width-24.0),22); AddSubview(_componentTitle);
        string[] lightNames={"Intensity","Range","Inner Cone","Outer Cone"};
        for(int i=0;i<4;i++)
        {
            var y=componentY-32-i*32;
            _lightLabels[i]=Label(lightNames[i],12,y,86,22); AddSubview(_lightLabels[i]);
            var field=new NSTextField(new CGRect(104,y-2,80,24)){StringValue="0"};
            int index=i; field.EditingEnded+=(_,_)=>ApplyLight(index); _lightFields[i]=field; AddSubview(field);
        }

        _selection.Changed+=Refresh; _world.Changed+=Refresh; Refresh();
    }

    private NSTextField Label(string text,nfloat x,nfloat y,nfloat w,nfloat h)=>new(new CGRect(x,y,w,h))
    {StringValue=text,Editable=false,Bezeled=false,DrawsBackground=false,TextColor=NSColor.FromRgb(210,210,214)};

    private void Refresh()
    {
        var selected=_selection.ActiveEntity;
        if(selected is null||!_world.Exists(selected.Value))
        {
            _title.StringValue="Nothing selected";
            SetLightControlsVisible(false);
            return;
        }

        var entity=selected.Value;
        _title.StringValue=_world.Has<NameComponent>(entity)?_world.Get<NameComponent>(entity).Name:$"Entity {entity.Id}";
        if(_world.Has<TransformComponent>(entity))
        {
            var t=_world.Get<TransformComponent>(entity);
            float[] values={t.Position.X,t.Position.Y,t.Position.Z,t.Rotation.X,t.Rotation.Y,t.Rotation.Z,t.Scale.X,t.Scale.Y,t.Scale.Z};
            for(int i=0;i<9;i++) _fields[i].StringValue=values[i].ToString("0.###",CultureInfo.InvariantCulture);
        }

        if(_world.Has<LightComponent>(entity))
        {
            var light=_world.Get<LightComponent>(entity);
            _componentTitle.StringValue=$"Light — {light.Type}";
            float[] values={light.Intensity,light.Range,light.InnerConeDegrees,light.OuterConeDegrees};
            for(int i=0;i<4;i++) _lightFields[i].StringValue=values[i].ToString("0.###",CultureInfo.InvariantCulture);
            _lightLabels[1].Hidden=light.Type==LightType.Directional; _lightFields[1].Hidden=light.Type==LightType.Directional;
            var spot=light.Type==LightType.Spot;
            _lightLabels[2].Hidden=!spot; _lightFields[2].Hidden=!spot;
            _lightLabels[3].Hidden=!spot; _lightFields[3].Hidden=!spot;
            _componentTitle.Hidden=false; _lightLabels[0].Hidden=false; _lightFields[0].Hidden=false;
        }
        else
        {
            SetLightControlsVisible(false);
        }
    }

    private void SetLightControlsVisible(bool visible)
    {
        _componentTitle.Hidden=!visible;
        for(int i=0;i<4;i++){_lightLabels[i].Hidden=!visible;_lightFields[i].Hidden=!visible;}
    }

    private void ApplyTransform(int index)
    {
        var selected=_selection.ActiveEntity;
        if(selected is null||!_world.Has<TransformComponent>(selected.Value)) return;
        if(!float.TryParse(_fields[index].StringValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)){Refresh();return;}
        var before=_world.Get<TransformComponent>(selected.Value); var after=before;
        if(index<3){var v=after.Position;if(index==0)v.X=value;else if(index==1)v.Y=value;else v.Z=value;after.Position=v;}
        else if(index<6){var v=after.Rotation;if(index==3)v.X=value;else if(index==4)v.Y=value;else v.Z=value;after.Rotation=v;}
        else{var v=after.Scale;if(index==6)v.X=value;else if(index==7)v.Y=value;else v.Z=value;after.Scale=v;}
        _commands.Execute(new SetTransformCommand(_world,selected.Value,before,after));
    }

    private void ApplyLight(int index)
    {
        var selected=_selection.ActiveEntity;
        if(selected is null||!_world.Has<LightComponent>(selected.Value)) return;
        if(!float.TryParse(_lightFields[index].StringValue,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)){Refresh();return;}
        var before=_world.Get<LightComponent>(selected.Value); var after=before;
        switch(index)
        {
            case 0: after.Intensity=MathF.Max(0,value); break;
            case 1: after.Range=MathF.Max(0.01f,value); break;
            case 2: after.InnerConeDegrees=Math.Clamp(value,0,179); break;
            case 3: after.OuterConeDegrees=Math.Clamp(value,0,179); break;
        }
        if(after.InnerConeDegrees>after.OuterConeDegrees) after.InnerConeDegrees=after.OuterConeDegrees;
        _commands.Execute(new SetLightCommand(_world,selected.Value,before,after));
    }
}
