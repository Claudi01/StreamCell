package app.streamcell;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.inputmethod.InputMethodManager;
import android.content.Context;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity {
    private static final String PREFS = "streamcell";
    private static final int BG = Color.rgb(16,17,21), PANEL = Color.rgb(27,25,36), PURPLE = Color.rgb(181,154,255), TEXT = Color.rgb(239,236,246), MUTED = Color.rgb(155,148,172);
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Runnable pollTask = new Runnable() { @Override public void run() { refreshDeck(); } };
    private EditText serverField, codeField;
    private String server = "", token = "", currentPage = "", deckSignature = "";
    private JSONObject deck;
    private boolean polling;
    private LinearLayout root;
    private TextView statusView;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        Window w = getWindow(); w.setStatusBarColor(BG); w.setNavigationBarColor(BG);
        token = getPreferences(MODE_PRIVATE).getString("token", "");
        server = getPreferences(MODE_PRIVATE).getString("server", "");
        getPreferences(MODE_PRIVATE).edit().remove("username").apply();
        if (token.length() > 0 && server.length() > 0) refreshDeck(); else showConnect("", "");
        receivePairIntent(getIntent());
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent); setIntent(intent); receivePairIntent(intent);
    }

    private void receivePairIntent(Intent intent) {
        Uri uri = intent == null ? null : intent.getData();
        if (uri == null || !"streamcell".equals(uri.getScheme()) || !"pair".equals(uri.getHost())) return;
        String host = uri.getQueryParameter("server"), code = uri.getQueryParameter("code");
        token = ""; getPreferences(MODE_PRIVATE).edit().remove("token").apply();
        showConnect(host == null ? "" : host, code == null ? "" : code);
    }

    private int dp(float v) { return (int)(v * getResources().getDisplayMetrics().density + 0.5f); }
    private GradientDrawable shape(int color, int stroke) {
        GradientDrawable d = new GradientDrawable(); d.setColor(color); d.setCornerRadius(dp(16));
        if (stroke != 0) d.setStroke(dp(1), stroke); return d;
    }
    private TextView text(String value, int size, int color) {
        TextView t = new TextView(this); t.setText(value); t.setTextSize(size); t.setTextColor(color);
        t.setGravity(Gravity.CENTER_VERTICAL); return t;
    }
    private EditText field(String hint, int inputType) {
        EditText e = new EditText(this); e.setSingleLine(true); e.setTextColor(TEXT); e.setHintTextColor(MUTED);
        e.setHint(hint); e.setInputType(inputType); e.setPadding(dp(14),0,dp(14),0);
        e.setBackground(shape(PANEL, Color.rgb(61,54,78))); LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1,dp(52)); lp.bottomMargin=dp(12); e.setLayoutParams(lp); return e;
    }
    private Button button(String label) {
        Button b = new Button(this); b.setText(label); b.setTextColor(TEXT); b.setAllCaps(false); b.setTextSize(14);
        b.setBackground(shape(Color.rgb(64,48,92), Color.rgb(120,91,165))); return b;
    }
    private void showConnect(String prefillServer, String prefillCode) {
        token = ""; deck = null; polling = false; handler.removeCallbacks(pollTask);
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true); scroll.setBackgroundColor(BG);
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL); page.setGravity(Gravity.CENTER_HORIZONTAL);
        page.setPadding(dp(24),dp(30),dp(24),dp(26)); scroll.addView(page);
        LinearLayout brand=new LinearLayout(this); brand.setGravity(Gravity.CENTER); ImageView brandIcon=new ImageView(this); brandIcon.setImageResource(R.mipmap.ic_launcher); brand.addView(brandIcon,new LinearLayout.LayoutParams(dp(44),dp(44))); TextView brandName=text("StreamCell",24,TEXT); brandName.setTypeface(null,Typeface.BOLD); LinearLayout.LayoutParams brandNameLp=new LinearLayout.LayoutParams(-2,-1); brandNameLp.leftMargin=dp(10); brand.addView(brandName,brandNameLp); page.addView(brand,new LinearLayout.LayoutParams(-1,dp(58)));
        TextView title=text("Conectar ao seu PC",23,TEXT); title.setTypeface(null,Typeface.BOLD); title.setGravity(Gravity.CENTER); LinearLayout.LayoutParams titleLp=new LinearLayout.LayoutParams(-1,dp(52)); titleLp.topMargin=dp(28); page.addView(title,titleLp);
        TextView help=text("Celular e computador precisam estar na mesma rede Wi-Fi.",13,MUTED); help.setGravity(Gravity.CENTER); page.addView(help,new LinearLayout.LayoutParams(-1,dp(48)));
        LinearLayout card=new LinearLayout(this); card.setOrientation(LinearLayout.VERTICAL); card.setPadding(dp(18),dp(20),dp(18),dp(18)); card.setBackground(shape(PANEL,Color.rgb(61,54,78)));
        LinearLayout.LayoutParams cardLp=new LinearLayout.LayoutParams(-1,-2); cardLp.topMargin=dp(20); page.addView(card,cardLp);
        serverField=field("Endereço do StreamCell no PC",android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI); serverField.setText(prefillServer.length()>0?prefillServer:server); card.addView(serverField);


        codeField=field("Código de conexão (8 caracteres)",android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS); codeField.setText(prefillCode); codeField.setLetterSpacing(0.2f); card.addView(codeField);
        Button connect=button("Conectar"); LinearLayout.LayoutParams connectLp=new LinearLayout.LayoutParams(-1,dp(50)); connectLp.topMargin=dp(4); card.addView(connect,connectLp);
        connect.setOnClickListener(v -> connect());
        TextView foot=text("O deck e as configurações do OBS ficam salvos no computador.",12,MUTED); foot.setGravity(Gravity.CENTER); LinearLayout.LayoutParams footLp=new LinearLayout.LayoutParams(-1,dp(64)); footLp.topMargin=dp(14); page.addView(foot,footLp);
        setContentView(scroll);
    }

    private void connect() {
        final String host=normalizeServer(serverField.getText().toString()), code=codeField.getText().toString().replaceAll("[\\s-]","").toUpperCase();
        if(host.length()==0||code.length()==0){toast("Informe o endereço e o código do StreamCell no PC.");return;}
        hideKeyboard();
        try { new URL(host); } catch(Exception e){toast("O endereço deve começar com http:// e apontar para o PC.");return;}
        worker.execute(() -> {
            try {
                JSONObject body=new JSONObject(); body.put("code",code);
                JSONObject result=send(host,"/api/pair",body,null);
                String newToken=result.optString("token","");
                if(newToken.length()==0)throw new Exception("O PC não enviou a conexão.");
                token=newToken; server=host; getPreferences(MODE_PRIVATE).edit().putString("token",token).putString("server",server).remove("username").apply();
                runOnUiThread(() -> { codeField.setText(""); toast("Conectado ao StreamCell."); refreshDeck(); });
            } catch(Exception e) { runOnUiThread(() -> toast(e.getMessage())); }
        });
    }

    private String normalizeServer(String value) {
        String s=value.trim(); if(s.length()==0)return "";
        if(!s.matches("(?i)^https?://.*")) s="http://"+s;
        while(s.endsWith("/"))s=s.substring(0,s.length()-1); return s;
    }

    private JSONObject send(String base, String path, JSONObject payload, String auth) throws Exception {
        HttpURLConnection c=(HttpURLConnection)new URL(base+path).openConnection();
        c.setConnectTimeout(5000); c.setReadTimeout(8000); c.setRequestProperty("Accept","application/json"); c.setRequestProperty("Cache-Control","no-store");
        if(auth!=null&&auth.length()>0)c.setRequestProperty("X-StreamCell-Token",auth);
        if(payload!=null){c.setRequestMethod("POST");c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json; charset=utf-8");byte[] bytes=payload.toString().getBytes(StandardCharsets.UTF_8);try(OutputStream out=c.getOutputStream()){out.write(bytes);}}
        int status=c.getResponseCode(); InputStream stream=status<400?c.getInputStream():c.getErrorStream(); StringBuilder json=new StringBuilder();
        try(BufferedReader reader=new BufferedReader(new InputStreamReader(stream,StandardCharsets.UTF_8))){String line;while((line=reader.readLine())!=null)json.append(line);}
        JSONObject result=new JSONObject(json.toString()); c.disconnect();
        if(status>=400)throw new Exception(result.optString("error","Não foi possível conectar ao PC."));
        return result;
    }

    private void refreshDeck() {
        if(polling)return; final String activeToken=token, activeServer=server;
        if(activeToken.length()==0||activeServer.length()==0)return;
        polling=true;
        worker.execute(() -> {
            try {
                JSONObject next=send(activeServer,"/api/deck",null,activeToken);
                String signature=next.toString();
                runOnUiThread(() -> {
                    polling=false; if(!activeToken.equals(token))return;
                    boolean changed=!signature.equals(deckSignature); deck=next;
                    if(currentPage.length()==0 && next.optJSONArray("pages")!=null && next.optJSONArray("pages").length()>0)currentPage=next.optJSONArray("pages").optJSONObject(0).optString("id","");
                    if(changed || root==null)showDeck(); else updateStatus();
                    handler.removeCallbacks(pollTask);handler.postDelayed(pollTask,2300);
                });
            } catch(Exception e) {
                runOnUiThread(() -> {
                    polling=false;
                    if(e.getMessage()!=null&&(e.getMessage().contains("Pareie")||e.getMessage().contains("conexão"))) {
                        getPreferences(MODE_PRIVATE).edit().remove("token").apply();token="";showConnect(server,"");toast("Conecte novamente usando o código do PC.");
                    } else { updateConnection(false);handler.postDelayed(pollTask,4000); }
                });
            }
        });
    }

    private JSONArray pages() { return deck==null?new JSONArray():deck.optJSONArray("pages"); }
    private JSONObject selectedPage() {
        JSONArray ps=pages(); if(ps==null||ps.length()==0)return null;
        for(int i=0;i<ps.length();i++){JSONObject p=ps.optJSONObject(i);if(p!=null&&currentPage.equals(p.optString("id")))return p;}
        return ps.optJSONObject(0);
    }

    private void showDeck() {
        if(deck==null)return;
        root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(BG);root.setPadding(dp(15),dp(13),dp(15),dp(12));
        LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);
        TextView brand=text("▦ StreamCell",19,TEXT);brand.setTypeface(null,Typeface.BOLD);header.addView(brand,new LinearLayout.LayoutParams(0,dp(46),1));
        statusView=text("● Conectado",11,Color.rgb(116,212,166));header.addView(statusView);
        Button disconnect=button("⋯");disconnect.setTextSize(22);disconnect.setPadding(0,0,0,dp(5));LinearLayout.LayoutParams dlp=new LinearLayout.LayoutParams(dp(48),dp(44));dlp.leftMargin=dp(6);header.addView(disconnect,dlp);
        disconnect.setOnClickListener(v -> new AlertDialog.Builder(this).setTitle("Desconectar").setMessage("Remover a conexão salva deste celular?").setNegativeButton("Cancelar",null).setPositiveButton("Desconectar",(a,b)->{getPreferences(MODE_PRIVATE).edit().remove("token").apply();token="";showConnect(server,"");}).show());
        root.addView(header);
        TextView title=text(deck.optString("title","Meu StreamCell"),22,TEXT);title.setTypeface(null,Typeface.BOLD);title.setPadding(dp(4),dp(5),dp(4),dp(13));root.addView(title);
        JSONObject page=selectedPage(); if(page!=null)currentPage=page.optString("id",currentPage);
        int columns=page==null?5:Math.max(2,Math.min(8,page.optInt("columns",5))), rows=page==null?3:Math.max(2,Math.min(6,page.optInt("rows",3)));
        GridLayout grid=new GridLayout(this);grid.setColumnCount(columns);grid.setRowCount(rows);grid.setUseDefaultMargins(false);
        LinearLayout.LayoutParams gridLp=new LinearLayout.LayoutParams(-1,0,1);root.addView(grid,gridLp);
        JSONArray keys=page==null?new JSONArray():page.optJSONArray("buttons");
        for(int slot=0;slot<columns*rows;slot++){
            JSONObject key=null;if(keys!=null)for(int i=0;i<keys.length();i++){JSONObject candidate=keys.optJSONObject(i);if(candidate!=null&&candidate.optInt("slot",-1)==slot){key=candidate;break;}}
            FrameLayout cell=createKey(key,slot);GridLayout.LayoutParams lp=new GridLayout.LayoutParams(GridLayout.spec(slot/columns,1,1f),GridLayout.spec(slot%columns,1,1f));lp.width=0;lp.height=0;lp.setGravity(Gravity.FILL);lp.setMargins(dp(5),dp(5),dp(5),dp(5));grid.addView(cell,lp);
        }
        LinearLayout nav=new LinearLayout(this);nav.setGravity(Gravity.CENTER);nav.setPadding(0,dp(8),0,dp(4));
        Button prev=button("‹");prev.setTextSize(24);nav.addView(prev,new LinearLayout.LayoutParams(dp(46),dp(45)));prev.setEnabled(pageIndex()>0);prev.setOnClickListener(v->{currentPage=pages().optJSONObject(pageIndex()-1).optString("id");showDeck();});
        LinearLayout tabs=new LinearLayout(this);tabs.setGravity(Gravity.CENTER);ScrollView tabScroll=new ScrollView(this);tabScroll.setHorizontalScrollBarEnabled(false);tabScroll.addView(tabs);nav.addView(tabScroll,new LinearLayout.LayoutParams(0,dp(52),1));
        JSONArray ps=pages();if(ps!=null)for(int i=0;i<ps.length();i++){JSONObject p=ps.optJSONObject(i);if(p==null)continue;Button tab=button(p.optString("name","Página"));tab.setTextSize(11);tab.setPadding(dp(10),0,dp(10),0);LinearLayout.LayoutParams tlp=new LinearLayout.LayoutParams(-2,dp(40));tlp.setMargins(dp(3),0,dp(3),0);tabs.addView(tab,tlp);tab.setBackground(shape(p.optString("id").equals(currentPage)?Color.rgb(55,43,73):PANEL,p.optString("id").equals(currentPage)?PURPLE:Color.rgb(50,47,61)));tab.setOnClickListener(v->{currentPage=p.optString("id");showDeck();});}
        Button next=button("›");next.setTextSize(24);nav.addView(next,new LinearLayout.LayoutParams(dp(46),dp(45)));next.setEnabled(pageIndex()<pages().length()-1);next.setOnClickListener(v->{currentPage=pages().optJSONObject(pageIndex()+1).optString("id");showDeck();});
        root.addView(nav,new LinearLayout.LayoutParams(-1,dp(55)));
        TextView footer=text("Configurado no computador  ·  OBS",10,MUTED);footer.setGravity(Gravity.CENTER);root.addView(footer,new LinearLayout.LayoutParams(-1,dp(27)));
        setContentView(root);deckSignature=deck.toString();updateStatus();
    }

    private int pageIndex(){JSONArray ps=pages();if(ps==null)return 0;for(int i=0;i<ps.length();i++)if(ps.optJSONObject(i)!=null&&currentPage.equals(ps.optJSONObject(i).optString("id")))return i;return 0;}
    private FrameLayout createKey(JSONObject key,int slot) {
        FrameLayout frame=new FrameLayout(this);String color=key==null?"#23212b":key.optString("color","#9180d7");
        int parsed;try{parsed=Color.parseColor(color);}catch(Exception e){parsed=Color.rgb(145,128,215);}
        frame.setBackground(shape(key==null?Color.rgb(27,25,36):Color.rgb(38,34,49),key==null?Color.rgb(45,42,53):parsed));
        frame.setForegroundGravity(Gravity.CENTER);frame.setClickable(key!=null);frame.setFocusable(key!=null);
        if(key==null){TextView empty=text("·",18,Color.rgb(68,65,79));empty.setGravity(Gravity.CENTER);frame.addView(empty,new FrameLayout.LayoutParams(-1,-1));return frame;}
        String label=key.optString("label","Sem título"),image=key.optString("image","");
        boolean photo=false;
        if(image.startsWith("data:image/"))try{
            byte[] bytes=Base64.decode(image.substring(image.indexOf(',')+1),Base64.DEFAULT);Bitmap bitmap=BitmapFactory.decodeByteArray(bytes,0,bytes.length);
            if(bitmap!=null){ImageView img=new ImageView(this);img.setImageBitmap(bitmap);img.setScaleType(ImageView.ScaleType.CENTER_CROP);frame.addView(img,new FrameLayout.LayoutParams(-1,-1));photo=true;}
        }catch(Exception ignored){}
        if(photo){TextView title=text(label,11,Color.WHITE);title.setGravity(Gravity.CENTER);title.setMaxLines(2);title.setPadding(dp(4),dp(15),dp(4),dp(5));title.setBackgroundColor(Color.argb(185,8,8,12));FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(-1,-2,Gravity.BOTTOM);frame.addView(title,lp);}
        else {LinearLayout inner=new LinearLayout(this);inner.setOrientation(LinearLayout.VERTICAL);inner.setGravity(Gravity.CENTER);TextView glyph=text(glyph(key.optString("icon")),28,parsed);glyph.setGravity(Gravity.CENTER);inner.addView(glyph,new LinearLayout.LayoutParams(-1,0,1));TextView title=text(label,12,TEXT);title.setGravity(Gravity.CENTER);title.setMaxLines(2);title.setPadding(dp(4),0,dp(4),dp(5));inner.addView(title,new LinearLayout.LayoutParams(-1,-2));frame.addView(inner,new FrameLayout.LayoutParams(-1,-1));}
        frame.setContentDescription(label);frame.setOnClickListener(v->press(key));return frame;
    }

    private String glyph(String icon) {
        if("mic".equals(icon))return "◉";if("mute".equals(icon))return "⌁";if("music".equals(icon)||"sound".equals(icon))return "♫";
        if("scene".equals(icon))return "▧";if("broadcast".equals(icon))return "◉";if("record".equals(icon))return "●";
        if("app".equals(icon))return "▣";if("globe".equals(icon)||"link".equals(icon))return "↗";if("camera".equals(icon))return "▣";
        if("stop".equals(icon))return "■";if("play".equals(icon))return "▶";if("pause".equals(icon))return "Ⅱ";if("folder".equals(icon))return "▱";
        if("chat".equals(icon))return "☏";if("heart".equals(icon))return "♥";if("coffee".equals(icon))return "☕";if("game".equals(icon))return "⌘";
        if("next".equals(icon))return "⏭";if("previous".equals(icon))return "⏮";if("volumeup".equals(icon))return "＋";if("volumedown".equals(icon))return "−";
        if("keyboard".equals(icon))return "⌨";if("monitor".equals(icon))return "▣";if("phone".equals(icon))return "▯";if("settings".equals(icon))return "⚙";
        if("star".equals(icon))return "★";return "⚡";
    }

    private void press(JSONObject key) {
        if(key.optBoolean("requiresConfirmation")||"obs_stream".equals(key.optString("type"))){
            new AlertDialog.Builder(this).setTitle("Executar esta ação?").setMessage(key.optString("label")).setNegativeButton("Cancelar",null).setPositiveButton("Executar",(d,w)->execute(key,true)).show();
        }else execute(key,false);
    }
    private void execute(JSONObject key,boolean confirmed) {
        worker.execute(()->{
            try{JSONObject body=new JSONObject();body.put("id",key.optString("id"));if(confirmed)body.put("confirmed",true);JSONObject result=send(server,"/api/execute",body,token);runOnUiThread(()->toast(result.optString("message",key.optString("label")+" executado.")));}
            catch(Exception e){runOnUiThread(()->toast(e.getMessage()));}
        });
    }
    private void updateStatus(){if(statusView!=null)statusView.setText("● Conectado");}
    private void updateConnection(boolean connected){if(statusView!=null){statusView.setText(connected?"● Conectado":"● Reconectando");statusView.setTextColor(connected?Color.rgb(116,212,166):Color.rgb(226,181,93));}}
    private void hideKeyboard(){try{((InputMethodManager)getSystemService(Context.INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(getCurrentFocus().getWindowToken(),0);}catch(Exception ignored){}}
    private void toast(String message){Toast.makeText(this,message==null?"Falha de conexão.":message,Toast.LENGTH_LONG).show();}

    @Override protected void onPause(){super.onPause();handler.removeCallbacks(pollTask);}
    @Override protected void onResume(){super.onResume();if(token.length()>0)handler.post(pollTask);}
    @Override protected void onDestroy(){super.onDestroy();worker.shutdownNow();handler.removeCallbacks(pollTask);}
}