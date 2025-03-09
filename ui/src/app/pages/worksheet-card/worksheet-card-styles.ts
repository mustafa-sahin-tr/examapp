export interface StyleConfig {
  background: string;
  subBackground: string;
  ribbonColor: string;
  ribbonLeftColor: string;
  ribbonRightColor: string;  
  ribbonShadowColor: string;
  iconColor: string;
  size: string;
}

export const CARDSTYLES : {[key:string]: StyleConfig} = {
    default: {
        background: '#00a69d',
        subBackground: '#D6E1F1',
        ribbonColor: '#00a69d',
        ribbonLeftColor: '#03817a',
        ribbonRightColor: '#03817a',
        ribbonShadowColor: '#02524e',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    primary: {
        background: '#F5921D',
        subBackground: '#D6E1F1',
        ribbonColor: '#c17217',
        ribbonLeftColor: '#c17217',
        ribbonRightColor: '#c17217',
        ribbonShadowColor: '#77470f',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    secondary: {
        background: '#00a69d',
        subBackground: '#F6931A',
        ribbonColor: '#F6931A',
        ribbonLeftColor: '#03817a',
        ribbonRightColor: '#03817a',
        ribbonShadowColor: '#02524e',
        iconColor: '#d6dbe1',
        size: '225px'
    },
    tertiary: {
        background: '#00a69d',
        subBackground: '#D6E1F1',
        ribbonColor: '#00a69d',
        ribbonLeftColor: '#03817a',
        ribbonRightColor: '#03817a',
        ribbonShadowColor: '#02524e',
        iconColor: '#d6dbe1',
        size: '225px'
    }
} 